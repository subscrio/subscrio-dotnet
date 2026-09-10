using FluentAssertions;
using Subscrio.Core;
using Subscrio.Core.Config;
using Subscrio.Core.Domain.ValueObjects;
using Subscrio.Core.Infrastructure.Database;
using Xunit;

namespace Subscrio.Core.Tests.E2E;

/// <summary>
/// Smoke test for SQL Server LocalDB schema install/verify.
/// Skips when LocalDB is not available on the machine.
/// </summary>
public class SqlServerSchemaTests
{
    private const string LocalDbConnectionString =
        "Server=(localdb)\\mssqllocaldb;Database=SubscrioSchemaSmoke;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";

    [Fact]
    public async Task InstallAndVerifySchema_WorksOnLocalDb()
    {
        Subscrio? subscrio = null;
        try
        {
            try
            {
                subscrio = new Subscrio(new SubscrioConfig
                {
                    Database = new DatabaseConfig
                    {
                        ConnectionString = LocalDbConnectionString,
                        Ssl = false,
                        PoolSize = 2,
                        DatabaseType = DatabaseType.SqlServer
                    },
                    AdminPassphrase = "localdb-smoke-passphrase"
                });

                // Probe connectivity before asserting — skip if LocalDB is unavailable
                await subscrio.VerifySchemaAsync();
            }
            catch (Exception)
            {
                // Skip when LocalDB is not installed or cannot be reached
                return;
            }

            await subscrio!.InstallSchemaAsync();
            var version = await subscrio.VerifySchemaAsync();
            version.Should().Be(SchemaInstaller.CurrentSchemaVersion);

            await subscrio.DropSchemaAsync();
            var afterDrop = await subscrio.VerifySchemaAsync();
            afterDrop.Should().BeNull();
        }
        finally
        {
            try
            {
                if (subscrio != null)
                {
                    await subscrio.DropSchemaAsync();
                }
            }
            catch
            {
                // Best-effort cleanup
            }

            subscrio?.Dispose();
        }
    }
}
