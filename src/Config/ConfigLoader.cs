using Subscrio.Core.Domain.ValueObjects;

namespace Subscrio.Core.Config;

public static class ConfigLoader
{
    public static SubscrioConfig LoadConfig()
    {
        var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL") 
            ?? throw new ApplicationException("DATABASE_URL environment variable is required");

        var databaseType = Environment.GetEnvironmentVariable("DATABASE_TYPE")?.ToLowerInvariant() switch
        {
            "sqlserver" => DatabaseType.SqlServer,
            "postgresql" or "postgres" => DatabaseType.PostgreSQL,
            _ => DatabaseType.PostgreSQL
        };

        var ssl = Environment.GetEnvironmentVariable("DATABASE_SSL")?.ToLowerInvariant() == "true";
        var poolSize = int.TryParse(Environment.GetEnvironmentVariable("DATABASE_POOL_SIZE"), out var size) ? size : 10;

        var adminPassphrase = Environment.GetEnvironmentVariable("ADMIN_PASSPHRASE");
        var stripeSecretKey = Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY");

        var logLevel = Environment.GetEnvironmentVariable("LOG_LEVEL")?.ToLowerInvariant() switch
        {
            "debug" => LogLevel.Debug,
            "info" => LogLevel.Info,
            "warn" => LogLevel.Warn,
            "error" => LogLevel.Error,
            _ => LogLevel.Info
        };

        return new SubscrioConfig
        {
            Database = new DatabaseConfig
            {
                ConnectionString = connectionString,
                Ssl = ssl,
                PoolSize = poolSize,
                DatabaseType = databaseType
            },
            AdminPassphrase = adminPassphrase,
            Stripe = stripeSecretKey != null ? new StripeConfig { SecretKey = stripeSecretKey } : null,
            Logging = new LoggingConfig { Level = logLevel }
        };
    }
}

