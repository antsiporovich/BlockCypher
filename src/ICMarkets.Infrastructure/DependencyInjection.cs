using ICMarkets.Application.Abstractions;
using ICMarkets.Infrastructure.BlockCypher;
using ICMarkets.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

        services.Configure<BlockCypherOptions>(config.GetSection(BlockCypherOptions.SectionName));
        services.AddHttpClient<IBlockCypherClient, BlockCypherClient>((sp, c) =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<BlockCypherOptions>>().Value;
            c.BaseAddress = new Uri(opts.BaseUrl);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("ICMarkets-BlockCypher-Assessment/1.0");
        });

        services.AddHealthChecks().AddDbContextCheck<AppDbContext>();

        return services;
    }
}
