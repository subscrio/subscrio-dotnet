using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Application.Hooks;
using Subscrio.Core.Domain.ValueObjects;

namespace Subscrio.Core.Config;

/// <summary>
/// Options for initial config sync: same inputs as ConfigSyncService (file path or JSON config).
/// If provided on SubscrioConfig, call RunInitialConfigSyncAsync() after construction to apply it.
/// </summary>
public class InitialConfigOptions
{
    /// <summary>Path to a JSON config file to sync from. If set, sync runs from file.</summary>
    public string? FilePath { get; init; }

    /// <summary>Config object to sync from. Used when FilePath is not set.</summary>
    public ConfigSyncDto? Config { get; init; }
}

public class SubscrioConfig
{
    public required DatabaseConfig Database { get; init; }
    public string? AdminPassphrase { get; init; }
    public StripeConfig? Stripe { get; init; }
    public LoggingConfig? Logging { get; init; }

    /// <summary>
    /// Optional initial config sync. If set, call RunInitialConfigSyncAsync() after construction to sync from file or JSON.
    /// </summary>
    public InitialConfigOptions? InitialConfig { get; init; }

    /// <summary>
    /// Optional before-mutation hooks registered at construction time.
    /// </summary>
    public SubscrioHooksOptions? Hooks { get; init; }
}

public class DatabaseConfig
{
    public required string ConnectionString { get; init; }
    public bool Ssl { get; init; }
    public int PoolSize { get; init; } = 10;
    public DatabaseType DatabaseType { get; init; } = DatabaseType.PostgreSQL;
}

public class StripeConfig
{
    public required string SecretKey { get; init; }
}

public class LoggingConfig
{
    public LogLevel Level { get; init; } = LogLevel.Info;
}

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error
}

