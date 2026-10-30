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
        // Application asks for a fresh unit of work per ingested item; the scope/DbContext
        // lifetime behind it is built here.
        services.AddSingleton<IUnitOfWorkFactory, IngestionUnitOfWorkFactory>();

        services.Configure<BlockCypherOptions>(config.GetSection(BlockCypherOptions.SectionName));
        services.AddSingleton<BlockCypherRateLimiter>();
        var blockCypherClient = services.AddHttpClient<IChainInfoSource, BlockCypherClient>((sp, c) =>
            {
                var opts = sp.GetRequiredService<IOptions<BlockCypherOptions>>().Value;
                c.BaseAddress = new Uri(opts.BaseUrl);
                // Timeouts are owned by the resilience pipeline and BlockCypherClient's
                // fetch budget. The built-in HttpClient.Timeout throws a
                // TaskCanceledException that looks exactly like caller cancellation.
                c.Timeout = Timeout.InfiniteTimeSpan;
                c.DefaultRequestHeaders.UserAgent.ParseAdd("ICMarkets-BlockCypher-Assessment/1.0");
            });

        // Pipeline order is registration order, outermost first:
        //   resilience (total timeout -> retry -> breaker -> attempt timeout) -> rate limiter -> network.
        // The limiter sits INSIDE the retry strategy, so every real attempt - including each
        // retry of a 429/5xx - takes a permit from the one process-wide budget.
        blockCypherClient
            .AddStandardResilienceHandler(resilience =>
            {
                // The standard handler's default predicate already treats 429, 5xx and
                // timeouts as transient and retries them with jittered exponential
                // backoff - and HttpRetryStrategyOptions.ShouldRetryAfterHeader is true
                // by default, so a Retry-After header (BOTH delta-seconds and HTTP-date
                // forms) already sets the retry delay. Deliberately NO custom
                // DelayGenerator: a hand-rolled one that read only Retry-After.Delta
                // silently ignored the date form and retried too early.
                resilience.Retry.MaxRetryAttempts = 3;
                resilience.Retry.BackoffType = DelayBackoffType.Exponential;
                resilience.Retry.UseJitter = true;
            });
        blockCypherClient.AddHttpMessageHandler(sp =>
            new RateLimitingHandler(sp.GetRequiredService<BlockCypherRateLimiter>()));

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
