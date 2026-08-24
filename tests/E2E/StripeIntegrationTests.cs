using FluentAssertions;
using Subscrio.Core;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Config;
using Subscrio.Core.Domain.ValueObjects;
using Subscrio.Core.Tests.Setup;
using Xunit;

namespace Subscrio.Core.Tests.E2E;

public class StripeIntegrationTests : IDisposable
{
    private readonly Subscrio _subscrio;
    private readonly TestFixtures _fixtures;

    public StripeIntegrationTests()
    {
        // Ensure database is initialized
        TestDatabaseAssemblyFixture.EnsureInitialized();
        
        // Create Subscrio instance with test database connection
        var connectionString = TestDatabaseAssemblyFixture.GetTestConnectionString();
        var config = new SubscrioConfig
        {
            Database = new DatabaseConfig
            {
                ConnectionString = connectionString,
                Ssl = false,
                PoolSize = 5, // Reduced pool size for tests
                DatabaseType = DatabaseType.PostgreSQL
            }
        };
        
        _subscrio = new Subscrio(config);
        _fixtures = new TestFixtures(_subscrio);
    }

    public void Dispose()
    {
        _subscrio?.Dispose();
    }

    [Fact(Skip = "Requires Stripe test environment")]
    public async Task ProcessesStripeSubscriptionCreatedEvent()
    {
        // This test requires Stripe test environment setup
        // Skipped by default - enable when Stripe integration is fully configured

        // Example test structure:
        // 1. Create customer with external billing ID
        // 2. Create plan with Stripe price mapping
        // 3. Process Stripe subscription.created event
        // 4. Verify subscription was created in Subscrio
    }

    [Fact(Skip = "Requires Stripe test environment")]
    public async Task ProcessesStripeSubscriptionUpdatedEvent()
    {
        // This test requires Stripe test environment setup
        // Skipped by default - enable when Stripe integration is fully configured
    }

    [Fact(Skip = "Requires Stripe test environment")]
    public async Task ProcessesStripeSubscriptionDeletedEvent()
    {
        // This test requires Stripe test environment setup
        // Skipped by default - enable when Stripe integration is fully configured
    }
}


