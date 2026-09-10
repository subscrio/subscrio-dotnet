using FluentAssertions;
using Subscrio.Core;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Config;
using Subscrio.Core.Domain.ValueObjects;
using Subscrio.Core.Tests.Setup;
using System.Collections.Generic;
using Xunit;

namespace Subscrio.Core.Tests.E2E;

public class FeatureCheckerMultiSubscriptionTests : IDisposable
{
    private readonly Subscrio _subscrio;
    private readonly TestFixtures _fixtures;

    public FeatureCheckerMultiSubscriptionTests()
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

    [Fact]
    public async Task ResolvesFeaturesForMultipleSubscriptionsEfficiently()
    {
        // Create a product with multiple plans
        var product = await _fixtures.CreateProductAsync(new Dictionary<string, object>
        {
            ["DisplayName"] = "Multi-Sub Test Product"
        });

        // Create feature
        var feature = await _fixtures.CreateFeatureAsync(new Dictionary<string, object>
        {
            ["DisplayName"] = "Test Feature",
            ["ValueType"] = "toggle",
            ["DefaultValue"] = "false"
        });

        await _subscrio.Products.AssociateFeatureAsync(product.Key, feature.Key);

        // Create multiple plans
        var plans = new List<PlanDto>();
        for (int i = 0; i < 3; i++)
        {
            var plan = await _fixtures.CreatePlanAsync(product.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = $"Plan {i}"
            });
            plans.Add(plan);
            await _subscrio.Plans.SetFeatureValueAsync(plan.Key, feature.Key, "true");
        }

        // Create customer with multiple subscriptions
        var customer = await _fixtures.CreateCustomerAsync(new Dictionary<string, object>
        {
            ["DisplayName"] = "Multi-Sub Test Customer"
        });

        // Create subscriptions for all plans
        var subscriptions = new List<SubscriptionDto>();
        for (int i = 0; i < plans.Count; i++)
        {
            var plan = plans[i];
            var billingCycle = await _fixtures.CreateBillingCycleAsync(plan.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = $"Multi-Sub Cycle {i}",
                ["DurationUnit"] = "months"
            });

            var subscription = await _fixtures.CreateSubscriptionAsync(
                customer.Key,
                billingCycle.Key
            );
            subscriptions.Add(subscription);
        }

        // Get all features for customer - should resolve efficiently
        var startTime = DateTime.UtcNow;
        var allFeatures = await _subscrio.FeatureChecker.GetAllFeaturesForCustomerAsync(
            customer.Key,
            product.Key
        );
        var endTime = DateTime.UtcNow;

        // Should complete quickly (batch resolution)
        var duration = endTime - startTime;
        duration.TotalSeconds.Should().BeLessThan(5); // Should be fast

        // Verify feature values are correct
        allFeatures.Should().ContainKey(feature.Key);
        allFeatures[feature.Key].Should().Be("true");
    }
}


