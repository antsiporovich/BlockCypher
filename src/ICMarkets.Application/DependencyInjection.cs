using System.Reflection;
using FluentValidation;
using ICMarkets.Application.Behaviors;
using ICMarkets.Application.Mapping;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace ICMarkets.Application;

public static class DependencyInjection
{
    private static readonly Assembly ThisAssembly = typeof(DependencyInjection).Assembly;

    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(ThisAssembly);

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(ThisAssembly);
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        services.AddSingleton<SnapshotMapper>();

        return services;
    }
}
