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
    /// If <paramref name="config"/> has <see cref="SubscrioConfig.InitialConfig"/> set,
    /// runs config sync once during this call (after ensuring schema exists).
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
        if (config.InitialConfig != null)
        {
            RunInitialConfigSyncOnce(config);
        }

        var descriptor = ServiceDescriptor.Describe(
            typeof(Subscrio),
            _ => new Subscrio(config),
            lifetime);

        services.Add(descriptor);
        return services;
    }

    private static void RunInitialConfigSyncOnce(SubscrioConfig config)
    {
        using var subscrio = new Subscrio(config);
        var schemaVersion = subscrio.VerifySchemaAsync().GetAwaiter().GetResult();
        if (schemaVersion == null)
        {
            subscrio.InstallSchemaAsync().GetAwaiter().GetResult();
        }
        subscrio.RunInitialConfigSyncAsync().GetAwaiter().GetResult();
    }
}
