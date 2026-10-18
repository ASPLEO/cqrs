using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Aspleo.Cqrs;

namespace Aspleo.Cqrs.DependencyInjection;

public static class RequestDispatcherServiceCollectionExtensions
{
    /// <summary>Registra os handlers e o dispatcher como scoped.</summary>
    public static IServiceCollection AddCqrs(this IServiceCollection services, params System.Reflection.Assembly[] assemblies)
    {
        services.AddRequestHandlers(assemblies);
        return services.AddRequestDispatcher();
    }

    public static IServiceCollection AddRequestDispatcher(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IDispatcher, RequestDispatcher>();
        return services;
    }
}

