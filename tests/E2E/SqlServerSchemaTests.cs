using FluentAssertions;
using Microsoft.Data.SqlClient;
using Subscrio.Core.Config;
using Subscrio.Core.Domain.ValueObjects;
using Subscrio.Core.Infrastructure.Database;

namespace Subscrio.Core.Tests.E2E;

public sealed class SqlServerSmokeFactAttribute : FactAttribute
{
    public SqlServerSmokeFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SUBSCRIO_SQLSERVER_TEST_SERVER")) &&
            Environment.GetEnvironmentVariable("SUBSCRIO_ENTITLEMENT_SQLSERVER") != "1")
            Skip = "Set SUBSCRIO_SQLSERVER_TEST_SERVER to run against SQL Server with Windows authentication.";
    }
}

public class SqlServerSchemaTests
{
    [SqlServerSmokeFact]
    public async Task InstallAndVerifySchema_WorksOnSqlServer()
    {
        var databaseName = "SubscrioSchemaSmoke_" + Guid.NewGuid().ToString("N");
        var connection = new SqlConnectionStringBuilder
        {
            DataSource = Environment.GetEnvironmentVariable("SUBSCRIO_SQLSERVER_TEST_SERVER") ?? "localhost",
            InitialCatalog = "master",
            IntegratedSecurity = true,
            TrustServerCertificate = true,
            ConnectTimeout = 5,
            ConnectRetryCount = 0,
            Pooling = false
        };
        await using var admin = new SqlConnection(connection.ConnectionString);
        await admin.OpenAsync();
        await using (var create = admin.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE [{databaseName}]";
            await create.ExecuteNonQueryAsync();
        }
        try
        {
            connection.InitialCatalog = databaseName;
            using var subscrio = new Subscrio(new SubscrioConfig
            {
                Database = new DatabaseConfig
                {
                    ConnectionString = connection.ConnectionString,
                    DatabaseType = DatabaseType.SqlServer
                },
                AdminPassphrase = "schema-smoke-passphrase"
            });
            (await subscrio.VerifySchemaAsync()).Should().BeNull();
            await subscrio.InstallSchemaAsync();
            (await subscrio.VerifySchemaAsync()).Should().Be(SchemaInstaller.CurrentSchemaVersion);
            await subscrio.DropSchemaAsync();
            await using var verifyDrop = admin.CreateCommand();
            verifyDrop.CommandText = "SELECT DB_ID(@name)";
            verifyDrop.Parameters.AddWithValue("@name", databaseName);
            (await verifyDrop.ExecuteScalarAsync()).Should().Be(DBNull.Value);
        }
        finally
        {
            await using var drop = admin.CreateCommand();
            drop.CommandText = $"IF DB_ID('{databaseName}') IS NOT NULL BEGIN ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]; END";
            await drop.ExecuteNonQueryAsync();
        }
    }
}
