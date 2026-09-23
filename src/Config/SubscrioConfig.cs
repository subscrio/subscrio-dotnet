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
    public string? FilePath
    {
        get; init;
    }

    /// <summary>Config object to sync from. Used when FilePath is not set.</summary>
    public ConfigSyncDto? Config
    {
        get; init;
    }
}

/// <summary>
/// Top-level configuration for constructing <see cref="Subscrio"/>.
/// </summary>
public class SubscrioConfig
{
    public IClock? Clock
    {
        get; init;
    }
    /// <summary>Database connection and provider settings.</summary>
    public required DatabaseConfig Database
    {
        get; init;
    }

    /// <summary>Optional passphrase for privileged schema operations (install/drop).</summary>
    public string? AdminPassphrase
    {
        get; init;
    }

    /// <summary>Optional Stripe API and webhook settings.</summary>
    public StripeConfig? Stripe
    {
        get; init;
    }

    /// <summary>Optional logging level for library diagnostics.</summary>
    public LoggingConfig? Logging
    {
        get; init;
    }

    /// <summary>
    /// Optional initial config sync. If set, call RunInitialConfigSyncAsync() after construction to sync from file or JSON.
    /// </summary>
    public InitialConfigOptions? InitialConfig
    {
        get; init;
    }

    /// <summary>
    /// Optional before-mutation hooks registered at construction time.
    /// </summary>
    public SubscrioHooksOptions? Hooks
    {
        get; init;
    }
}

/// <summary>
/// Database connection options used by Subscrio.
/// </summary>
public class DatabaseConfig
{
    /// <summary>EF Core / provider connection string.</summary>
    public required string ConnectionString
    {
        get; init;
    }

    /// <summary>Whether to require SSL for the database connection.</summary>
    public bool Ssl
    {
        get; init;
    }

    /// <summary>Connection pool size hint (default 10).</summary>
    public int PoolSize { get; init; } = 10;

    /// <summary>Database provider (PostgreSQL or SQL Server).</summary>
    public DatabaseType DatabaseType { get; init; } = DatabaseType.PostgreSQL;
}

/// <summary>
/// Stripe integration settings.
/// </summary>
public class StripeConfig
{
    /// <summary>Stripe secret API key.</summary>
    public required string SecretKey
    {
        get; init;
    }

    /// <summary>
    /// Optional Stripe webhook endpoint secret (<c>whsec_...</c>).
    /// When set, use <see cref="ConstructStripeEvent"/> to verify signatures.
    /// </summary>
    public string? WebhookSecret
    {
        get; init;
    }

    /// <summary>
    /// Verifies the Stripe webhook signature and constructs an <see cref="Stripe.Event"/>.
    /// Requires <see cref="WebhookSecret"/> to be set.
    /// </summary>
    /// <param name="json">Raw request body.</param>
    /// <param name="signatureHeader">Value of the <c>Stripe-Signature</c> header.</param>
    /// <returns>A verified Stripe event.</returns>
    /// <exception cref="InvalidOperationException">Thrown when <see cref="WebhookSecret"/> is not configured.</exception>
    public Stripe.Event ConstructStripeEvent(string json, string signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(WebhookSecret))
        {
            throw new InvalidOperationException(
                "StripeConfig.WebhookSecret is not set. Configure the webhook endpoint secret before calling ConstructStripeEvent.");
        }

        return Stripe.EventUtility.ConstructEvent(json, signatureHeader, WebhookSecret);
    }
}

/// <summary>
/// Library logging options.
/// </summary>
public class LoggingConfig
{
    /// <summary>Minimum log level (default Info).</summary>
    public LogLevel Level { get; init; } = LogLevel.Info;
}

/// <summary>
/// Supported library log levels.
/// </summary>
public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error
}

