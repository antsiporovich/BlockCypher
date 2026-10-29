using ICMarkets.Application.Abstractions;
using ICMarkets.Infrastructure.BackgroundServices;
using ICMarkets.Infrastructure.BlockCypher;
using ICMarkets.Infrastructure.Messaging;
using ICMarkets.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Polly;

namespace ICMarkets.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var provider = config["Database:Provider"] ?? "SqlServer";
        var connectionString = config.GetConnectionString("Default");

        services.AddDbContext<AppDbContext>(opt =>
        {
            if (provider == "Sqlite")
                opt.UseSqlite(connectionString);
            else
                opt.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure());
        });

        services.AddScoped<IBlockchainRepository, BlockchainRepository>();
        // AppDbContext already implements IUnitOfWork - expose it as that
        // interface too, resolving to the same scoped instance (same DbContext).
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());

        services.Configure<BlockCypherOptions>(config.GetSection(BlockCypherOptions.SectionName));
        services.AddHttpClient<IBlockCypherClient, BlockCypherClient>((sp, c) =>
            {
                var opts = sp.GetRequiredService<IOptions<BlockCypherOptions>>().Value;
                c.BaseAddress = new Uri(opts.BaseUrl);
                c.DefaultRequestHeaders.UserAgent.ParseAdd("ICMarkets-BlockCypher-Assessment/1.0");
            })
            .AddStandardResilienceHandler(resilience =>
            {
                // The standard handler's default predicate already treats 429,
                // 5xx and timeouts as transient and retries them with jittered
                // exponential backoff. The one thing it doesn't do by default is
                // honor a Retry-After header, so wire that in explicitly -
                // falls back to the computed backoff when the header is absent.
                resilience.Retry.MaxRetryAttempts = 3;
                resilience.Retry.BackoffType = DelayBackoffType.Exponential;
                resilience.Retry.UseJitter = true;
                resilience.Retry.DelayGenerator = args =>
                {
                    var retryAfter = args.Outcome.Result?.Headers.RetryAfter?.Delta;
                    return ValueTask.FromResult(retryAfter);
                };
            });

        services.Configure<IngestionOptions>(config.GetSection(IngestionOptions.SectionName));
        services.AddHostedService<ChainIngestionWorker>();

        // Event-driven outbound edge: SnapshotIngested events are written to
        // the outbox transactionally (AppDbContext.SaveChangesAsync) and
        // drained to RabbitMQ by OutboxPublisherWorker, which no-ops when
        // Outbox:Enabled is false - no RabbitMQ required for local/SQLite
        // dev or the functional tests.
        services.Configure<OutboxOptions>(config.GetSection(OutboxOptions.SectionName));
        services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();
        services.AddHostedService<OutboxPublisherWorker>();

        services.AddHealthChecks().AddDbContextCheck<AppDbContext>();

        return services;
    }
}
