using FluentAssertions;
using Subscrio.Core.Config;
using Subscrio.Core.Domain.ValueObjects;
using Xunit;

namespace Subscrio.Core.Tests.Unit;

public class ConfigLoaderTests
{
    private static readonly string[] EnvKeys =
    [
        "DATABASE_URL",
        "DATABASE_TYPE",
        "DATABASE_SSL",
        "DATABASE_POOL_SIZE",
        "ADMIN_PASSPHRASE",
        "STRIPE_SECRET_KEY",
        "STRIPE_WEBHOOK_SECRET",
        "LOG_LEVEL"
    ];

    [Fact]
    public void LoadConfig_MapsEnvironmentVariables()
    {
        var previous = CaptureEnv();
        try
        {
            Environment.SetEnvironmentVariable("DATABASE_URL", "Host=localhost;Database=subscrio_cfg_test");
            Environment.SetEnvironmentVariable("DATABASE_TYPE", "sqlserver");
            Environment.SetEnvironmentVariable("DATABASE_SSL", "true");
            Environment.SetEnvironmentVariable("DATABASE_POOL_SIZE", "5");
            Environment.SetEnvironmentVariable("ADMIN_PASSPHRASE", "admin-secret");
            Environment.SetEnvironmentVariable("STRIPE_SECRET_KEY", "sk_test_loader");
            Environment.SetEnvironmentVariable("STRIPE_WEBHOOK_SECRET", "whsec_loader");
            Environment.SetEnvironmentVariable("LOG_LEVEL", "debug");

            var config = ConfigLoader.LoadConfig();

            config.Database.ConnectionString.Should().Be("Host=localhost;Database=subscrio_cfg_test");
            config.Database.DatabaseType.Should().Be(DatabaseType.SqlServer);
            config.Database.Ssl.Should().BeTrue();
            config.Database.PoolSize.Should().Be(5);
            config.AdminPassphrase.Should().Be("admin-secret");
            config.Stripe.Should().NotBeNull();
            config.Stripe!.SecretKey.Should().Be("sk_test_loader");
            config.Stripe.WebhookSecret.Should().Be("whsec_loader");
            config.Logging.Should().NotBeNull();
            config.Logging!.Level.Should().Be(LogLevel.Debug);
        }
        finally
        {
            RestoreEnv(previous);
        }
    }

    [Fact]
    public void LoadConfig_Throws_WhenDatabaseUrlMissing()
    {
        var previous = CaptureEnv();
        try
        {
            foreach (var key in EnvKeys)
            {
                Environment.SetEnvironmentVariable(key, null);
            }

            Action act = () => ConfigLoader.LoadConfig();
            act.Should().Throw<ApplicationException>()
                .WithMessage("*DATABASE_URL*");
        }
        finally
        {
            RestoreEnv(previous);
        }
    }

    private static Dictionary<string, string?> CaptureEnv()
    {
        var values = new Dictionary<string, string?>();
        foreach (var key in EnvKeys)
        {
            values[key] = Environment.GetEnvironmentVariable(key);
        }
        return values;
    }

    private static void RestoreEnv(Dictionary<string, string?> previous)
    {
        foreach (var (key, value) in previous)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }
}
