using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Subscrio.Core.Config;
using Subscrio.Core.Domain.ValueObjects;
using Subscrio.Core.Infrastructure.Database;
using Subscrio.Core.Tests.Setup;
namespace Subscrio.Core.Tests.E2E;

public class EntitlementMigrationTests
{
    private static async Task Execute(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
    private static async Task Legacy(Func<DatabaseConfig, SubscrioDbContext, SchemaInstaller, Task> work)
    {
        var isSql = Environment.GetEnvironmentVariable("SUBSCRIO_ENTITLEMENT_SQLSERVER") == "1";
        var name = "SubscrioMigrationVerify_" + Guid.NewGuid().ToString("N");
        DatabaseConfig config;
        DbConnection admin;
        if (isSql)
        {
            var builder = new SqlConnectionStringBuilder("Server=localhost;Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=True");
            admin = new SqlConnection(builder.ConnectionString);
            builder.InitialCatalog = name;
            config = new()
            {
                ConnectionString = builder.ConnectionString,
                DatabaseType = DatabaseType.SqlServer
            };
        }
        else
        {
            TestDatabaseAssemblyFixture.EnsureInitialized();
            var builder = new NpgsqlConnectionStringBuilder(TestDatabaseAssemblyFixture.GetTestConnectionString()) { Database = "postgres" };
            admin = new NpgsqlConnection(builder.ConnectionString);
            builder.Database = name;
            config = new()
            {
                ConnectionString = builder.ConnectionString
            };
        }
        await using (admin)
        {
            await admin.OpenAsync();
            await Execute(admin, isSql ? $"CREATE DATABASE [{name}]" : $"CREATE DATABASE \"{name}\"");
            try
            {
                var initialized = DatabaseInitializer.InitializeDatabase(config);
                await using (var db = initialized.DbContext)
                {
                    await db.Database.OpenConnectionAsync();
                    var connection = db.Database.GetDbConnection();
                    var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Schema1_1", isSql ? "sqlserver.sql" : "postgres.sql"));
                    foreach (var batch in Regex.Split(script, @"^GO\s*$", RegexOptions.Multiline))
                        if (!string.IsNullOrWhiteSpace(batch))
                            await Execute(connection, batch);
                    await Execute(connection, $"INSERT INTO subscrio.system_config(config_key,config_value,encrypted) VALUES('schema_version','1.1.0',{(isSql ? "0" : "false")})");
                    var key = isSql ? "[key]" : "key";
                    await Execute(connection, $"INSERT INTO subscrio.products({key},display_name,status) VALUES('legacy-product','Legacy','active'); INSERT INTO subscrio.features({key},display_name,value_type,default_value,status) VALUES('legacy-limit','Limit','numeric','3','active'); INSERT INTO subscrio.product_features(product_id,feature_id) SELECT p.id,f.id FROM subscrio.products p CROSS JOIN subscrio.features f");
                    await work(config, db, new SchemaInstaller(db));
                }
                if (initialized.DataSource != null)
                    await initialized.DataSource.DisposeAsync();
            }
            finally { if (isSql) { SqlConnection.ClearAllPools(); await Execute(admin, $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]"); } else { NpgsqlConnection.ClearAllPools(); await Execute(admin, $"DROP DATABASE \"{name}\" WITH (FORCE)"); } }
        }
    }
    [Fact]
    public async Task ActualHistoricalSchemaUpgradesUnderConcurrentMigration() => await Legacy(async (config, db, installer) =>
    {
        var second = DatabaseInitializer.InitializeDatabase(config);
        await using (var other = second.DbContext)
        {
            var applied = await Task.WhenAll(installer.MigrateAsync(), new SchemaInstaller(other).MigrateAsync());
            Assert.Equal(new[] { 0, 3 }, applied.Order().ToArray());
        }
        if (second.DataSource != null)
            await second.DataSource.DisposeAsync();
        Assert.Equal("1.4.0", await installer.VerifyAsync());
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT composition_rule,cross_subscription_rule FROM subscrio.product_features";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("override_wins", reader.GetString(0));
        Assert.Equal("legacy", reader.GetString(1));
    });
    [Fact]
    public async Task DdlAndSchemaVersionRollbackOnDatabaseFailure() => await Legacy(async (config, db, installer) =>
    {
        await Execute(db.Database.GetDbConnection(), "CREATE TABLE subscrio.credit_grants(id BIGINT PRIMARY KEY)");
        await Assert.ThrowsAnyAsync<Exception>(() => installer.MigrateAsync());
        Assert.Equal("1.1.0", await installer.VerifyAsync());
        await using (var command = db.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='subscrio' AND table_name='addons'";
            Assert.Equal(0, Convert.ToInt32(await command.ExecuteScalarAsync()));
        }
        await Execute(db.Database.GetDbConnection(), "DROP TABLE subscrio.credit_grants");
        Assert.Equal(3, await installer.MigrateAsync());
    });
    [Fact]
    public async Task NewerSchemaIsRefusedBeforeInstallWrites() => await Legacy(async (config, db, installer) =>
    {
        await Execute(db.Database.GetDbConnection(), "UPDATE subscrio.system_config SET config_value='99.0.0' WHERE config_key='schema_version'");
        await Assert.ThrowsAsync<global::Subscrio.Core.Application.Errors.ValidationException>(() => installer.InstallAsync("must-not-write"));
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM subscrio.system_config WHERE config_key='admin_passphrase_hash'";
        Assert.Equal(0, Convert.ToInt32(await command.ExecuteScalarAsync()));
    });
}
