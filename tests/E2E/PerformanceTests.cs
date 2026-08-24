using FluentAssertions;
using Subscrio.Core;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Config;
using Subscrio.Core.Domain.ValueObjects;
using Subscrio.Core.Tests.Setup;
using System.Collections.Generic;
using Xunit;

namespace Subscrio.Core.Tests.E2E;

public class PerformanceTests : IDisposable
{
    private readonly Subscrio _subscrio;
    private readonly TestFixtures _fixtures;

    public PerformanceTests()
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
    public async Task ListsProductsEfficiently()
    {
        // Create multiple products
        for (int i = 0; i < 10; i++)
        {
            await _fixtures.CreateProductAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = $"Performance Product {i}"
            });
        }

        // List products - should complete quickly
        var startTime = DateTime.UtcNow;
        var products = await _subscrio.Products.ListProductsAsync();
        var endTime = DateTime.UtcNow;

        var duration = endTime - startTime;
        duration.TotalSeconds.Should().BeLessThan(2); // Should be fast

        products.Should().HaveCountGreaterThanOrEqualTo(10);
    }

    [Fact]
    public async Task ResolvesFeaturesEfficiently()
    {
        // Create product with features
        var product = await _fixtures.CreateProductAsync(new Dictionary<string, object>
        {
            ["DisplayName"] = "Performance Feature Product"
        });

        // Create multiple features
        var features = new List<FeatureDto>();
        for (int i = 0; i < 5; i++)
        {
            var feature = await _fixtures.CreateFeatureAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = $"Performance Feature {i}",
                ["ValueType"] = "numeric",
                ["DefaultValue"] = "10"
            });
            features.Add(feature);
            await _subscrio.Products.AssociateFeatureAsync(product.Key, feature.Key);
        }

        // Create plan and set feature values
        var plan = await _fixtures.CreatePlanAsync(product.Key, new Dictionary<string, object>
        {
            ["DisplayName"] = "Performance Plan"
        });

        foreach (var feature in features)
        {
            await _subscrio.Plans.SetFeatureValueAsync(plan.Key, feature.Key, "50");
        }

        // Create customer and subscription
        var customer = await _fixtures.CreateCustomerAsync(new Dictionary<string, object>
        {
            ["DisplayName"] = "Performance Customer"
        });

        var billingCycle = await _fixtures.CreateBillingCycleAsync(plan.Key, new Dictionary<string, object>
        {
            ["DisplayName"] = "Performance Cycle",
                ["DurationUnit"] = "months"
        });

        await _fixtures.CreateSubscriptionAsync(
            customer.Key,
            billingCycle.Key
        );

        // Resolve all features - should complete quickly
        var startTime = DateTime.UtcNow;
        var allFeatures = await _subscrio.FeatureChecker.GetAllFeaturesForCustomerAsync(
            customer.Key,
            product.Key
        );
        var endTime = DateTime.UtcNow;

        var duration = endTime - startTime;
        duration.TotalSeconds.Should().BeLessThan(2); // Should be fast

        allFeatures.Should().HaveCount(5);
    }
}


