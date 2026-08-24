using Microsoft.EntityFrameworkCore;
using Subscrio.Core.Infrastructure.Database;
using BCrypt.Net;
using Npgsql;

namespace Subscrio.Core.Infrastructure.Database;

public class SchemaInstaller
{
    private readonly SubscrioDbContext _db;

    public SchemaInstaller(SubscrioDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Install database schema using EF Core EnsureCreated
    /// </summary>
    public async Task InstallAsync(string? adminPassphrase = null)
    {
        // Retry logic for transient connection issues
        int maxRetries = 3;
        Exception? lastException = null;
        
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                // Check if schema already exists
                var schemaExists = await VerifyAsync();
                
                if (schemaExists == null)
                {
                    // Schema doesn't exist, create it
                    await _db.Database.EnsureCreatedAsync();
                }

                // Create the subscription status view
                await CreateSubscriptionStatusViewAsync();

                // Setup initial system configuration
                await SetupInitialConfigAsync(adminPassphrase);
                
                return; // Success
            }
            catch (Exception ex) when (attempt < maxRetries - 1)
            {
                lastException = ex;
                
                // Check if this is a transient connection error
                bool isTransientError = ex is NpgsqlException ||
                    ex is System.Net.Sockets.SocketException ||
                    ex.Message.Contains("forcibly closed", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("transient failure", StringComparison.OrdinalIgnoreCase) ||
                    ex.InnerException is NpgsqlException ||
                    ex.InnerException is System.Net.Sockets.SocketException ||
                    (ex.InnerException?.Message.Contains("forcibly closed", StringComparison.OrdinalIgnoreCase) == true);
                
                if (!isTransientError)
                {
                    // Not a transient error, rethrow immediately
                    throw;
                }
                
                // Wait before retry (exponential backoff)
                await Task.Delay(300 * (attempt + 1));
                
                // Reconnect if needed
                try
                {
                    var connection = _db.Database.GetDbConnection();
                    if (connection.State != System.Data.ConnectionState.Open)
                    {
                        if (connection.State != System.Data.ConnectionState.Closed)
                        {
                            await connection.CloseAsync();
                        }
                        await connection.OpenAsync();
                    }
                }
                catch
                {
                    // Ignore connection errors during retry setup
                }
            }
        }
        
        // If we get here, all retries failed - throw the last exception
        if (lastException != null)
        {
            throw lastException;
        }
        throw new InvalidOperationException("Schema installation failed after retries");
    }

    /// <summary>
    /// Verify schema installation by checking if tables exist
    /// </summary>
    public async Task<string?> VerifyAsync()
    {
        try
        {
            // Check if system_config table exists and has schema_version
            var schemaVersion = await _db.SystemConfig
                .Where(sc => sc.ConfigKey == "schema_version")
                .Select(sc => sc.ConfigValue)
                .FirstOrDefaultAsync();

            return schemaVersion;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Run pending database migrations
    /// </summary>
    public async Task<int> MigrateAsync()
    {
        var pendingMigrations = await _db.Database.GetPendingMigrationsAsync();
        var count = pendingMigrations.Count();

        if (count > 0)
        {
            await _db.Database.MigrateAsync();
            
            // Recreate view after migrations to ensure it's up to date
            await CreateSubscriptionStatusViewAsync();
            
            // Update schema version
            await UpdateSchemaVersionAsync();
        }

        return count;
    }

    /// <summary>
    /// Drop all database tables (WARNING: Destructive!)
    /// </summary>
    public async Task DropSchemaAsync()
    {
        // Drop view first
        await _db.Database.ExecuteSqlRawAsync(@"
            DROP VIEW IF EXISTS subscrio.subscription_status_view CASCADE;
        ");
        
        await _db.Database.EnsureDeletedAsync();
    }

    private async Task CreateSubscriptionStatusViewAsync()
    {
        // Drop view if exists
        await _db.Database.ExecuteSqlRawAsync(@"
            DROP VIEW IF EXISTS subscrio.subscription_status_view CASCADE;
        ");

        // Create view
        await _db.Database.ExecuteSqlRawAsync(@"
            CREATE VIEW subscrio.subscription_status_view AS
            SELECT
                s.id,
                s.key,
                s.customer_id,
                s.plan_id,
                s.billing_cycle_id,
                s.activation_date,
                s.expiration_date,
                s.cancellation_date,
                s.trial_end_date,
                s.current_period_start,
                s.current_period_end,
                s.stripe_subscription_id,
                s.metadata,
                s.created_at,
                s.updated_at,
                s.is_archived,
                s.transitioned_at,
                CASE
                    WHEN s.cancellation_date IS NOT NULL AND s.cancellation_date > NOW() THEN 'cancellation_pending'
                    WHEN s.cancellation_date IS NOT NULL AND s.cancellation_date <= NOW() THEN 'cancelled'
                    WHEN s.expiration_date IS NOT NULL AND s.expiration_date <= NOW() THEN 'expired'
                    WHEN s.activation_date IS NOT NULL AND s.activation_date > NOW() THEN 'pending'
                    WHEN s.trial_end_date IS NOT NULL AND s.trial_end_date > NOW() THEN 'trial'
                    ELSE 'active'
                END AS computed_status
            FROM subscrio.subscriptions s;
        ");
    }

    private async Task SetupInitialConfigAsync(string? adminPassphrase)
    {
        // Check if schema_version already exists
        var existingVersion = await _db.SystemConfig
            .Where(sc => sc.ConfigKey == "schema_version")
            .FirstOrDefaultAsync();

        if (existingVersion == null)
        {
            // Set initial schema version
            _db.SystemConfig.Add(new SystemConfigRecord
            {
                ConfigKey = "schema_version",
                ConfigValue = "1.1.0",
                Encrypted = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        // Set admin passphrase if provided
        if (!string.IsNullOrEmpty(adminPassphrase))
        {
            var existingPassphrase = await _db.SystemConfig
                .Where(sc => sc.ConfigKey == "admin_passphrase_hash")
                .FirstOrDefaultAsync();

            var hashedPassphrase = BCrypt.Net.BCrypt.HashPassword(adminPassphrase);

            if (existingPassphrase == null)
            {
                _db.SystemConfig.Add(new SystemConfigRecord
                {
                    ConfigKey = "admin_passphrase_hash",
                    ConfigValue = hashedPassphrase,
                    Encrypted = false,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            else
            {
                existingPassphrase.ConfigValue = hashedPassphrase;
                existingPassphrase.UpdatedAt = DateTime.UtcNow;
                _db.SystemConfig.Update(existingPassphrase);
            }
        }

        await _db.SaveChangesAsync();
    }

    private async Task UpdateSchemaVersionAsync()
    {
        var version = await _db.SystemConfig
            .Where(sc => sc.ConfigKey == "schema_version")
            .FirstOrDefaultAsync();

        if (version != null)
        {
            // Update to current version (matches TypeScript version)
            version.ConfigValue = "1.1.0";
            version.UpdatedAt = DateTime.UtcNow;
            _db.SystemConfig.Update(version);
            await _db.SaveChangesAsync();
        }
    }
}

