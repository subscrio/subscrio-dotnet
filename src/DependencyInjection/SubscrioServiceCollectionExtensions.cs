using Microsoft.Extensions.DependencyInjection;
using Subscrio.Core;
using Subscrio.Core.Config;

namespace Subscrio.Core.DependencyInjection;

/// <summary>
/// Extension methods for registering Subscrio with <see cref="IServiceCollection"/>.
/// </summary>
public static class SubscrioServiceCollectionExtensions
{
    /// <summary>
    /// Registers Subscrio in the service collection with the specified lifetime.
    /// Does not install schema or run <see cref="SubscrioConfig.InitialConfig"/> sync.
    /// When <see cref="SubscrioConfig.InitialConfig"/> is set, call
    /// <see cref="Subscrio.InstallSchemaAsync"/> (if needed) and
    /// <see cref="Subscrio.RunInitialConfigSyncAsync"/> explicitly after building the host
    /// to avoid sync-over-async deadlocks during DI registration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="config">Subscrio configuration (database, optional Stripe, optional InitialConfig, etc.).</param>
    /// <param name="lifetime">Service lifetime. Use <see cref="ServiceLifetime.Scoped"/> for web apps.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSubscrio(
        this IServiceCollection services,
        SubscrioConfig config,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
    {
        var descriptor = ServiceDescriptor.Describe(
            typeof(Subscrio),
            _ => new Subscrio(config),
            lifetime);

        services.Add(descriptor);
        return services;
    }
}
