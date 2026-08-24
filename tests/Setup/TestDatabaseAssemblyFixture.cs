using Subscrio.Core;
using Subscrio.Core.Config;
using Subscrio.Core.Domain.ValueObjects;

namespace Subscrio.Core.Tests.Setup;

/// <summary>
/// Assembly-level fixture for test database setup/teardown
/// Uses static initialization to ensure database is created once per test run
/// </summary>
public static class TestDatabaseAssemblyFixture
{
    private static readonly Lazy<Task> _initializationTask;
    private static string? _dbName;
    private static bool _initialized = false;
    private static readonly object _lock = new object();

    static TestDatabaseAssemblyFixture()
    {
        _initializationTask = new Lazy<Task>(InitializeDatabaseAsync, LazyThreadSafetyMode.ExecutionAndPublication);
        
        // Register cleanup on process exit (best effort)
        AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(_dbName))
            {
                try
                {
                    // Fire-and-forget async cleanup
                    _ = Task.Run(async () =>
                    {
                        await TestDatabase.TeardownTestDatabaseAsync(_dbName);
                    });
                }
                catch
                {
                    // Ignore errors during process exit
                }
            }
        };
    }

    /// <summary>
    /// Gets the test connection string (triggers initialization if not already done)
    /// </summary>
    public static string GetTestConnectionString()
    {
        // Ensure initialization completes before returning connection string
        _initializationTask.Value.GetAwaiter().GetResult();
        return TestDatabase.GetTestConnectionString();
    }

    /// <summary>
    /// Ensures database is initialized (call this from test constructors if needed)
    /// </summary>
    public static void EnsureInitialized()
    {
        // Trigger initialization
        _initializationTask.Value.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Performs the actual database initialization
    /// </summary>
    private static async Task InitializeDatabaseAsync()
    {
        lock (_lock)
        {
            if (_initialized)
            {
                return;
            }
        }

        Console.WriteLine("🏗️  Setting up test database...");

        // Clean up any dangling test databases first
        await TestDatabase.CleanupDanglingTestDatabasesAsync();

        var context = await TestDatabase.SetupTestDatabaseAsync();
        
        lock (_lock)
        {
            _dbName = context.DbName;
            _initialized = true;
        }

        Console.WriteLine($"✅ Test database ready: {context.DbName}\n");
    }

    /// <summary>
    /// Tears down the test database (can be called manually if needed)
    /// Note: Database will also be cleaned up by dangling database cleanup on next run
    /// </summary>
    public static async Task TeardownAsync()
    {
        string? dbNameToTeardown;
        lock (_lock)
        {
            dbNameToTeardown = _dbName;
            _dbName = null;
            _initialized = false;
        }

        if (!string.IsNullOrEmpty(dbNameToTeardown))
        {
            Console.WriteLine("\n🧹 Tearing down test database...");
            await TestDatabase.TeardownTestDatabaseAsync(dbNameToTeardown);
            Console.WriteLine("✅ Test database cleaned up");
        }
    }
}

