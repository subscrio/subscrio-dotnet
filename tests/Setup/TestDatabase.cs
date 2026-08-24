using Microsoft.Extensions.Configuration;
using Npgsql;
using Subscrio.Core;
using Subscrio.Core.Config;

namespace Subscrio.Core.Tests.Setup;

public class TestContext
{
    public required string DbName { get; init; }
    public required string ConnectionString { get; init; }
    public required Subscrio Subscrio { get; init; }
}

public static class TestDatabase
{
    // Include framework version in database name to avoid conflicts when running multiple frameworks in parallel
    // Format: subscrio_test_net80, subscrio_test_net90, subscrio_test_net100
    private static readonly string TestDbName = GetFrameworkSpecificDbName();
    private static IConfiguration? _configuration;

    private static string GetFrameworkSpecificDbName()
    {
        // Extract framework version from RuntimeInformation (e.g., ".NET 8.0.0" -> "net80")
        var framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
        var match = System.Text.RegularExpressions.Regex.Match(framework, @"\.NET (\d+)\.(\d+)");
        if (match.Success)
        {
            var major = match.Groups[1].Value;
            var minor = match.Groups[2].Value;
            return $"subscrio_test_net{major}{minor}";
        }
        // Fallback to process ID if framework version can't be determined
        return $"subscrio_test_{System.Diagnostics.Process.GetCurrentProcess().Id}";
    }

    /// <summary>
    /// Get configuration from appsettings.json or environment variables
    /// </summary>
    private static IConfiguration GetConfiguration()
    {
        if (_configuration != null)
            return _configuration;

        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables(); // Environment variables override appsettings.json

        _configuration = builder.Build();
        return _configuration;
    }

    /// <summary>
    /// Get test database connection string from configuration
    /// Priority: Environment variable > appsettings.json > default
    /// </summary>
    private static string GetTestDatabaseConnectionString()
    {
        var config = GetConfiguration();
        
        // Check environment variable first (highest priority)
        var envConnectionString = Environment.GetEnvironmentVariable("TEST_DATABASE_URL");
        if (!string.IsNullOrEmpty(envConnectionString))
            return envConnectionString;

        // Check appsettings.json
        var configConnectionString = config["TestDatabase:ConnectionString"];
        if (!string.IsNullOrEmpty(configConnectionString))
            return configConnectionString;

        // Default fallback
        return "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";
    }

    /// <summary>
    /// Get whether to keep test database after tests
    /// Priority: Environment variable > appsettings.json > default (false)
    /// </summary>
    private static bool GetKeepTestDb()
    {
        var config = GetConfiguration();
        
        // Check environment variable first (highest priority)
        var envKeepTestDb = Environment.GetEnvironmentVariable("KEEP_TEST_DB");
        if (!string.IsNullOrEmpty(envKeepTestDb))
            return envKeepTestDb.Equals("true", StringComparison.OrdinalIgnoreCase);

        // Check appsettings.json
        var configKeepTestDb = config["TestDatabase:KeepTestDb"];
        if (!string.IsNullOrEmpty(configKeepTestDb))
            return bool.TryParse(configKeepTestDb, out var result) && result;

        // Default fallback
        return false;
    }

    /// <summary>
    /// Setup a fresh test database
    /// Uses framework-specific name: subscrio_test_net80, subscrio_test_net90, etc.
    /// This allows multiple .NET versions to run tests in parallel without conflicts.
    /// </summary>
    public static async Task<TestContext> SetupTestDatabaseAsync()
    {
        var baseUrl = GetTestDatabaseConnectionString();

        // Extract connection details for admin connection
        var adminBuilder = new NpgsqlConnectionStringBuilder(baseUrl)
        {
            Database = "postgres" // Connect to postgres database to create test DB
        };

        // Connect to postgres database
        await using var adminConnection = new NpgsqlConnection(adminBuilder.ConnectionString);
        await adminConnection.OpenAsync();

        // Check if database exists first
        bool dbExists = false;
        try
        {
            await using (var checkCmd = new NpgsqlCommand($@"
                SELECT 1 FROM pg_database WHERE datname = '{TestDbName}'", adminConnection))
            {
                var result = await checkCmd.ExecuteScalarAsync();
                dbExists = result != null;
            }
        }
        catch
        {
            // Ignore errors
        }

        // Only terminate connections if database exists
        if (dbExists)
        {
            // Terminate any existing connections to the test database before dropping
            await using (var terminateCmd = new NpgsqlCommand($@"
                SELECT pg_terminate_backend(pg_stat_activity.pid)
                FROM pg_stat_activity
                WHERE pg_stat_activity.datname = '{TestDbName}'
                  AND pid <> pg_backend_pid()", adminConnection))
            {
                try
                {
                    await terminateCmd.ExecuteNonQueryAsync();
                    // Small delay to allow connections to close gracefully
                    await Task.Delay(200);
                }
                catch
                {
                    // Ignore errors
                }
            }

            // Drop existing test database (with retry)
            int retries = 3;
            for (int i = 0; i < retries; i++)
            {
                try
                {
                    await using (var dropCmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS {TestDbName}", adminConnection))
                    {
                        await dropCmd.ExecuteNonQueryAsync();
                    }
                    break; // Success
                }
                catch (Npgsql.PostgresException ex) when (ex.SqlState == "55006" && i < retries - 1)
                {
                    // Database still has connections, terminate again and wait
                    try
                    {
                        await using (var terminateCmd = new NpgsqlCommand($@"
                            SELECT pg_terminate_backend(pg_stat_activity.pid)
                            FROM pg_stat_activity
                            WHERE pg_stat_activity.datname = '{TestDbName}'
                              AND pid <> pg_backend_pid()", adminConnection))
                        {
                            await terminateCmd.ExecuteNonQueryAsync();
                        }
                    }
                    catch { }
                    await Task.Delay(300 * (i + 1));
                }
                catch
                {
                    // Other errors - continue
                    break;
                }
            }
        }

        // Create fresh test database
        await using (var createCmd = new NpgsqlCommand($"CREATE DATABASE {TestDbName}", adminConnection))
        {
            await createCmd.ExecuteNonQueryAsync();
        }

        // Build connection string for test database
        var testBuilder = new NpgsqlConnectionStringBuilder(baseUrl)
        {
            Database = TestDbName
        };
        var connectionString = testBuilder.ConnectionString;

        // Initialize Subscrio with test database
        var config = new SubscrioConfig
        {
            Database = new DatabaseConfig
            {
                ConnectionString = connectionString,
                Ssl = false,
                PoolSize = 10,
                DatabaseType = Domain.ValueObjects.DatabaseType.PostgreSQL
            }
        };

        var subscrio = new Subscrio(config);

        // Install schema using public API
        await subscrio.InstallSchemaAsync("test-admin-passphrase");

        return new TestContext
        {
            DbName = TestDbName,
            ConnectionString = connectionString,
            Subscrio = subscrio
        };
    }

    /// <summary>
    /// Teardown test database
    /// Set KEEP_TEST_DB=true or TestDatabase:KeepTestDb=true to preserve databases for debugging
    /// </summary>
    public static async Task TeardownTestDatabaseAsync(string dbName)
    {
        // Check if we should keep the database for debugging
        if (GetKeepTestDb())
        {
            var baseUrl = GetTestDatabaseConnectionString();

            var builder = new NpgsqlConnectionStringBuilder(baseUrl)
            {
                Database = dbName
            };

            Console.WriteLine($"\n🔍 Test database preserved for debugging:");
            Console.WriteLine($"   Database: {dbName}");
            Console.WriteLine($"   Connection: {builder.ConnectionString}");
            Console.WriteLine($"   To connect: psql {builder.ConnectionString}");
            Console.WriteLine($"   To drop: DROP DATABASE {dbName};");
            return;
        }

        var adminUrl = GetTestDatabaseConnectionString();

        var adminBuilder = new NpgsqlConnectionStringBuilder(adminUrl)
        {
            Database = "postgres"
        };

        await using var adminConnection = new NpgsqlConnection(adminBuilder.ConnectionString);
        await adminConnection.OpenAsync();

        // Terminate connections
        await using (var terminateCmd = new NpgsqlCommand($@"
            SELECT pg_terminate_backend(pg_stat_activity.pid)
            FROM pg_stat_activity
            WHERE pg_stat_activity.datname = '{dbName}'
              AND pid <> pg_backend_pid()", adminConnection))
        {
            await terminateCmd.ExecuteNonQueryAsync();
        }

        // Drop database
        await using (var dropCmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS {dbName}", adminConnection))
        {
            await dropCmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Clean up any dangling test databases
    /// Set KEEP_TEST_DB=true or TestDatabase:KeepTestDb=true to skip cleanup
    /// </summary>
    public static async Task CleanupDanglingTestDatabasesAsync()
    {
        // Skip cleanup if we're preserving test databases
        if (GetKeepTestDb())
        {
            Console.WriteLine("🔍 Skipping test database cleanup (KEEP_TEST_DB=true or TestDatabase:KeepTestDb=true)");
            return;
        }

        var adminUrl = GetTestDatabaseConnectionString();

        var adminBuilder = new NpgsqlConnectionStringBuilder(adminUrl)
        {
            Database = "postgres"
        };

        await using var adminConnection = new NpgsqlConnection(adminBuilder.ConnectionString);
        await adminConnection.OpenAsync();

        // Find all test databases
        await using var findCmd = new NpgsqlCommand(@"
            SELECT datname 
            FROM pg_database 
            WHERE datname LIKE 'subscrio_test_%'", adminConnection);

        await using var reader = await findCmd.ExecuteReaderAsync();
        var dbNames = new List<string>();

        while (await reader.ReadAsync())
        {
            dbNames.Add(reader.GetString(0));
        }

        await reader.CloseAsync();

        if (dbNames.Count > 0)
        {
            Console.WriteLine($"🧹 Cleaning up {dbNames.Count} dangling test databases...");

            foreach (var dbName in dbNames)
            {
                try
                {
                    // Terminate connections to this database
                    await using (var terminateCmd = new NpgsqlCommand($@"
                        SELECT pg_terminate_backend(pg_stat_activity.pid)
                        FROM pg_stat_activity
                        WHERE pg_stat_activity.datname = '{dbName}'
                          AND pid <> pg_backend_pid()", adminConnection))
                    {
                        await terminateCmd.ExecuteNonQueryAsync();
                    }

                    // Drop the database
                    await using (var dropCmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS {dbName}", adminConnection))
                    {
                        await dropCmd.ExecuteNonQueryAsync();
                    }

                    Console.WriteLine($"   ✓ Dropped {dbName}");
                }
                catch (Exception error)
                {
                    Console.WriteLine($"   ⚠️  Failed to drop {dbName}: {error.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Get test connection string
    /// </summary>
    public static string GetTestConnectionString()
    {
        var baseUrl = GetTestDatabaseConnectionString();

        var builder = new NpgsqlConnectionStringBuilder(baseUrl)
        {
            Database = TestDbName
        };

        return builder.ConnectionString;
    }
}

