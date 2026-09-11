using FluentAssertions;
using Npgsql;
using Subscrio.Core;
using Subscrio.Core.Config;
using Subscrio.Core.Domain.ValueObjects;
using Subscrio.Core.Infrastructure.Database;
using Subscrio.Core.Tests.Setup;
using Xunit;

namespace Subscrio.Core.Tests.E2E;

/// <summary>
/// Schema install/migrate/drop against an isolated Postgres database so the shared
/// assembly fixture DB is never dropped mid-run.
/// </summary>
public class SchemaLifecycleTests
{
    private const string AdminPassphrase = "test-admin-passphrase";

    [Fact]
    public async Task InstallThenMigrate_ReturnsAndVerifyStillNonNull()
    {
        await using var scope = await IsolatedSchemaDb.CreateAsync();
        var version = await scope.Subscrio.VerifySchemaAsync();
        version.Should().Be(SchemaInstaller.CurrentSchemaVersion);

        var migrateResult = await scope.Subscrio.MigrateAsync();
        migrateResult.Should().BeGreaterThanOrEqualTo(0);

        var afterMigrate = await scope.Subscrio.VerifySchemaAsync();
        afterMigrate.Should().NotBeNullOrEmpty();
        afterMigrate.Should().Be(SchemaInstaller.CurrentSchemaVersion);
    }

    [Fact]
    public async Task DropSchemaAsync_WithPassphrase_RemovesSchema()
    {
        await using var scope = await IsolatedSchemaDb.CreateAsync();
        (await scope.Subscrio.VerifySchemaAsync()).Should().NotBeNull();

        await scope.Subscrio.DropSchemaAsync(AdminPassphrase);

        var afterDrop = await scope.Subscrio.VerifySchemaAsync();
        afterDrop.Should().BeNull();
    }

    private sealed class IsolatedSchemaDb : IAsyncDisposable
    {
        private readonly string _dbName;
        private readonly string _adminConnectionString;

        public Subscrio Subscrio { get; }

        private IsolatedSchemaDb(string dbName, string adminConnectionString, Subscrio subscrio)
        {
            _dbName = dbName;
            _adminConnectionString = adminConnectionString;
            Subscrio = subscrio;
        }

        public static async Task<IsolatedSchemaDb> CreateAsync()
        {
            // Reuse fixture admin host settings without touching the shared test DB.
            TestDatabaseAssemblyFixture.EnsureInitialized();
            var shared = new NpgsqlConnectionStringBuilder(TestDatabaseAssemblyFixture.GetTestConnectionString());
            var adminBuilder = new NpgsqlConnectionStringBuilder(shared.ConnectionString)
            {
                Database = "postgres"
            };
            var dbName = $"subscrio_test_schema_{Guid.NewGuid():N}";

            await using (var admin = new NpgsqlConnection(adminBuilder.ConnectionString))
            {
                await admin.OpenAsync();
                await using var create = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", admin);
                await create.ExecuteNonQueryAsync();
            }

            var testBuilder = new NpgsqlConnectionStringBuilder(shared.ConnectionString)
            {
                Database = dbName
            };

            var subscrio = new Subscrio(new SubscrioConfig
            {
                Database = new DatabaseConfig
                {
                    ConnectionString = testBuilder.ConnectionString,
                    Ssl = false,
                    PoolSize = 2,
                    DatabaseType = DatabaseType.PostgreSQL
                },
                AdminPassphrase = AdminPassphrase
            });

            await subscrio.InstallSchemaAsync(AdminPassphrase);

            return new IsolatedSchemaDb(dbName, adminBuilder.ConnectionString, subscrio);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                Subscrio.Dispose();
            }
            catch
            {
                // Best-effort
            }

            try
            {
                await using var admin = new NpgsqlConnection(_adminConnectionString);
                await admin.OpenAsync();
                await using (var terminate = new NpgsqlCommand($@"
                    SELECT pg_terminate_backend(pg_stat_activity.pid)
                    FROM pg_stat_activity
                    WHERE pg_stat_activity.datname = '{_dbName}'
                      AND pid <> pg_backend_pid()", admin))
                {
                    await terminate.ExecuteNonQueryAsync();
                }

                await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_dbName}\"", admin);
                await drop.ExecuteNonQueryAsync();
            }
            catch
            {
                // Best-effort cleanup; dangling cleanup will catch leftovers
            }
        }
    }
}
