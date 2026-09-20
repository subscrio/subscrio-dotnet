using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Subscrio.Core.Application.Errors;
using BCrypt.Net;
using Npgsql;

namespace Subscrio.Core.Infrastructure.Database;

public class SchemaInstaller
{
    public const string CurrentSchemaVersion = "1.1.0";

    private readonly SubscrioDbContext _db;

    public SchemaInstaller(SubscrioDbContext db)
    {
        _db = db;
    }

    private bool IsSqlServer => _db.Database.IsSqlServer();

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
                if (!IsTransientError(ex))
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
    /// Verify schema installation by checking if tables exist.
    /// Returns null when the schema is missing; rethrows unexpected errors.
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
        catch (Exception ex) when (IsSchemaMissingException(ex))
        {
            return null;
        }
    }

    /// <summary>
    /// Run pending database migrations and refresh versioned objects (e.g. subscription_status_view).
    /// When no EF migrations exist, still recreates the view and bumps schema_version when needed.
    /// </summary>
    public async Task<int> MigrateAsync()
    {
        var pendingMigrations = await _db.Database.GetPendingMigrationsAsync();
        var efCount = pendingMigrations.Count();
        var previousVersion = await VerifyAsync();

        if (efCount > 0)
        {
            await _db.Database.MigrateAsync();
        }

        // Always refresh the view so EnsureCreated installs stay current without EF migrations.
        await CreateSubscriptionStatusViewAsync();
        await UpdateSchemaVersionAsync();

        if (efCount > 0)
        {
            return efCount;
        }

        // Count a versioned upgrade when the stored version differed (or was missing).
        return previousVersion != CurrentSchemaVersion ? 1 : 0;
    }

    /// <summary>
    /// Drop all database tables (WARNING: Destructive!).
    /// When an admin passphrase hash exists, <paramref name="adminPassphrase"/> must match.
    /// </summary>
    public async Task DropSchemaAsync(string? adminPassphrase = null)
    {
        await VerifyAdminPassphraseAsync(adminPassphrase);

        await DropSubscriptionStatusViewAsync();
        
        await _db.Database.EnsureDeletedAsync();
    }

    private async Task VerifyAdminPassphraseAsync(string? adminPassphrase)
    {
        SystemConfigRecord? existingHash;
        try
        {
            existingHash = await _db.SystemConfig
                .Where(sc => sc.ConfigKey == "admin_passphrase_hash")
                .FirstOrDefaultAsync();
        }
        catch (Exception ex) when (IsSchemaMissingException(ex))
        {
            // Schema may not exist yet
            return;
        }

        if (existingHash == null || string.IsNullOrEmpty(existingHash.ConfigValue))
        {
            return;
        }

        if (string.IsNullOrEmpty(adminPassphrase) ||
            !BCrypt.Net.BCrypt.Verify(adminPassphrase, existingHash.ConfigValue))
        {
            throw new ValidationException(
                "Admin passphrase is required and must match the configured passphrase to drop the schema.");
        }
    }

    private async Task DropSubscriptionStatusViewAsync()
    {
        if (IsSqlServer)
        {
            await _db.Database.ExecuteSqlRawAsync(@"
                IF OBJECT_ID(N'subscrio.subscription_status_view', N'V') IS NOT NULL
                    DROP VIEW subscrio.subscription_status_view;
            ");
        }
        else
        {
            await _db.Database.ExecuteSqlRawAsync(@"
                DROP VIEW IF EXISTS subscrio.subscription_status_view CASCADE;
            ");
        }
    }

    private async Task CreateSubscriptionStatusViewAsync()
    {
        await DropSubscriptionStatusViewAsync();

        var nowExpr = IsSqlServer ? "SYSUTCDATETIME()" : "NOW()";
        var keyExpr = IsSqlServer ? "s.[key] AS [key]" : "s.key";

        // nowExpr and keyExpr are fixed provider-specific SQL fragments, not user input.
        var createViewSql =
            "CREATE VIEW subscrio.subscription_status_view AS " +
            "SELECT " +
            $"s.id, {keyExpr}, s.customer_id, s.plan_id, s.billing_cycle_id, " +
            "s.activation_date, s.expiration_date, s.cancellation_date, s.trial_end_date, " +
            "s.current_period_start, s.current_period_end, s.stripe_subscription_id, " +
            "s.metadata, s.created_at, s.updated_at, s.is_archived, s.transitioned_at, " +
            "CASE " +
            "WHEN s.cancellation_date IS NOT NULL AND s.cancellation_date > " + nowExpr + " THEN 'cancellation_pending' " +
            "WHEN s.cancellation_date IS NOT NULL AND s.cancellation_date <= " + nowExpr + " THEN 'cancelled' " +
            "WHEN s.expiration_date IS NOT NULL AND s.expiration_date <= " + nowExpr + " THEN 'expired' " +
            "WHEN s.activation_date IS NOT NULL AND s.activation_date > " + nowExpr + " THEN 'pending' " +
            "WHEN s.trial_end_date IS NOT NULL AND s.trial_end_date > " + nowExpr + " THEN 'trial' " +
            "ELSE 'active' " +
            "END AS computed_status " +
            "FROM subscrio.subscriptions s;";

        await _db.Database.ExecuteSqlRawAsync(createViewSql);
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
                ConfigValue = CurrentSchemaVersion,
                Encrypted = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        // Set admin passphrase only when none exists — never overwrite without verification
        if (!string.IsNullOrEmpty(adminPassphrase))
        {
            var existingPassphrase = await _db.SystemConfig
                .Where(sc => sc.ConfigKey == "admin_passphrase_hash")
                .FirstOrDefaultAsync();

            if (existingPassphrase == null)
            {
                var hashedPassphrase = BCrypt.Net.BCrypt.HashPassword(adminPassphrase);
                _db.SystemConfig.Add(new SystemConfigRecord
                {
                    ConfigKey = "admin_passphrase_hash",
                    ConfigValue = hashedPassphrase,
                    Encrypted = false,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            // Existing hash is left unchanged (DropSchemaAsync / destructive ops verify against it)
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
            version.ConfigValue = CurrentSchemaVersion;
            version.UpdatedAt = DateTime.UtcNow;
            _db.SystemConfig.Update(version);
            await _db.SaveChangesAsync();
        }
        else
        {
            _db.SystemConfig.Add(new SystemConfigRecord
            {
                ConfigKey = "schema_version",
                ConfigValue = CurrentSchemaVersion,
                Encrypted = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }
    }

    private static bool IsTransientError(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException!)
        {
            if (e is NpgsqlException || e is SqlException || e is System.Net.Sockets.SocketException)
            {
                return true;
            }

            if (e.Message.Contains("forcibly closed", StringComparison.OrdinalIgnoreCase) ||
                e.Message.Contains("transient failure", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSchemaMissingException(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException!)
        {
            if (e is PostgresException { SqlState: PostgresErrorCodes.UndefinedTable })
            {
                return true;
            }

            // SQL Server: Invalid object name (208)
            if (e is SqlException sqlEx && sqlEx.Number == 208)
            {
                return true;
            }

            var message = e.Message;
            if (message.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
