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
        services.AddDbContext<AppDbContext>(opt =>
            opt.UseSqlServer(config.GetConnectionString("Default")));

        services.AddScoped<IBlockchainRepository, BlockchainRepository>();

        services.AddHttpClient<IBlockCypherClient, BlockCypherClient>(c =>
        {
            c.BaseAddress = new Uri(config["BlockCypher:BaseUrl"]!);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("ICMarkets-BlockCypher-Assessment/1.0");
        });

        services.AddHealthChecks().AddDbContextCheck<AppDbContext>();

        return services;
    }
}
