using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Aspleo.Cqrs;

namespace Aspleo.Cqrs.DependencyInjection;

/// <summary>Descobre e registra handlers fechados como scoped.</summary>
public static class RequestHandlerRegistrationExtensions
{
    public static IServiceCollection AddRequestHandlers(this IServiceCollection services, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assemblies);
        if (assemblies.Any(assembly => assembly is null))
            throw new ArgumentException("As assemblies dos handlers não podem ser nulas.", nameof(assemblies));

        var registrations = assemblies.Distinct()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.IsClass && !type.IsAbstract && !type.ContainsGenericParameters)
            .SelectMany(type => type.GetInterfaces()
                .Where(IsHandler)
                .Select(contract => new { Contract = contract, Implementation = type }))
            .Distinct()
            .ToArray();

        // Valida tudo antes de alterar o IServiceCollection; chamadas repetidas sao idempotentes.
        foreach (var group in registrations.GroupBy(item => item.Contract))
        {
            var implementation = group.First().Implementation;
            if (group.Count() != 1 || services.Count(descriptor => descriptor.ServiceType == group.Key) > 1 || services.Any(descriptor => descriptor.ServiceType == group.Key
                && (descriptor.IsKeyedService || descriptor.ImplementationType != implementation || descriptor.Lifetime != ServiceLifetime.Scoped)))
                throw new InvalidOperationException($"Mais de um handler registrado para {group.Key}.");
        }

        foreach (var registration in registrations)
        {
            if (!services.Any(descriptor => descriptor.ServiceType == registration.Contract))
                services.AddScoped(registration.Contract, registration.Implementation);
        }

        return services;
    }

    private static bool IsHandler(Type contract)
        => contract.IsGenericType && (contract.GetGenericTypeDefinition() == typeof(IRequestHandler<>)
            || contract.GetGenericTypeDefinition() == typeof(IRequestHandler<,>));
}

