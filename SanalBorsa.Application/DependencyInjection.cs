using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SanalBorsa.Application.Common;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Application.Common.Behaviors;
using System.Reflection;

namespace SanalBorsa.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
        });

        services.AddValidatorsFromAssembly(assembly);

        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        services.AddSingleton<MarketDataCacheVersion>();
        services.AddSingleton<IClock, SystemClock>();

        return services;
    }
}
