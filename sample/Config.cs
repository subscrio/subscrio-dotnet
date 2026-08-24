using Subscrio.Core.Config;
using Subscrio.Core.Domain.ValueObjects;

namespace Subscrio.Sample;

public static class SampleConfig
{
    public static SubscrioConfig LoadConfig()
    {
        // Use hardcoded connection string (can be overridden by environment variable)
        var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL") 
            ?? "Host=localhost;Port=5432;Database=subscrio_sample;Username=postgres;Password=your-password";

        return new SubscrioConfig
        {
            Database = new DatabaseConfig
            {
                ConnectionString = connectionString,
                DatabaseType = DatabaseType.PostgreSQL,
                Ssl = false,
                PoolSize = 10
            }
        };
    }
}


