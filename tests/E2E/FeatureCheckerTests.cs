using FluentAssertions;
using Subscrio.Core;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Config;
using Subscrio.Core.Domain.ValueObjects;
using Subscrio.Core.Tests.Setup;
using System.Collections.Generic;
using Xunit;

namespace Subscrio.Core.Tests.E2E;

public class FeatureCheckerTests : IDisposable
{
    private readonly Subscrio _subscrio;
    private readonly TestFixtures _fixtures;

    public FeatureCheckerTests()
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

    public class BasicResolution : FeatureCheckerTests
    {
        public BasicResolution() : base() { }

        [Fact]
        public async Task ResolvesFeatureFromDefaultValue()
        {
            // Create product and feature
            var product = await _fixtures.CreateProductAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Basic Product"
            });

            var feature = await _fixtures.CreateFeatureAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Max Users",
                ["ValueType"] = "numeric",
                ["DefaultValue"] = "10"
            });

            await _subscrio.Products.AssociateFeatureAsync(product.Key, feature.Key);

            // Create customer with no subscription
            var customer = await _fixtures.CreateCustomerAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Test Customer"
            });

            // Should return feature default
            var value = await _subscrio.FeatureChecker.GetValueForCustomerAsync<string>(
                customer.Key,
                product.Key,
                feature.Key
            );
            value.Should().Be("10");
        }

        [Fact]
        public async Task ResolvesFeatureFromPlanValue()
        {
            var product = await _fixtures.CreateProductAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Plan Product"
            });

            var feature = await _fixtures.CreateFeatureAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Storage GB",
                ["ValueType"] = "numeric",
                ["DefaultValue"] = "5"
            });

            await _subscrio.Products.AssociateFeatureAsync(product.Key, feature.Key);

            var plan = await _fixtures.CreatePlanAsync(product.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Pro Plan"
            });

            var billingCycle = await _fixtures.CreateBillingCycleAsync(plan.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Test Monthly FC",
                ["DurationUnit"] = "months"
            });

            // Set plan value
            await _subscrio.Plans.SetFeatureValueAsync(plan.Key, feature.Key, "50");

            var customer = await _fixtures.CreateCustomerAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Plan Customer"
            });

            var subscription = await _fixtures.CreateSubscriptionAsync(
                customer.Key,
                billingCycle.Key
            );

            // Should return plan value
            var value = await _subscrio.FeatureChecker.GetValueForCustomerAsync<string>(
                customer.Key,
                product.Key,
                feature.Key
            );
            value.Should().Be("50");
        }

        [Fact]
        public async Task ResolvesFeatureFromSubscriptionOverride()
        {
            var product = await _fixtures.CreateProductAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Override Product"
            });

            var feature = await _fixtures.CreateFeatureAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "API Calls",
                ["ValueType"] = "numeric",
                ["DefaultValue"] = "100"
            });

            await _subscrio.Products.AssociateFeatureAsync(product.Key, feature.Key);

            var plan = await _fixtures.CreatePlanAsync(product.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Basic"
            });

            var billingCycle = await _fixtures.CreateBillingCycleAsync(plan.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Test Monthly FC",
                ["DurationUnit"] = "months"
            });

            await _subscrio.Plans.SetFeatureValueAsync(plan.Key, feature.Key, "1000");

            var customer = await _fixtures.CreateCustomerAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Override Customer"
            });

            var subscription = await _fixtures.CreateSubscriptionAsync(
                customer.Key,
                billingCycle.Key
            );

            // Add subscription override
            await _subscrio.Subscriptions.AddFeatureOverrideAsync(
                subscription.Key,
                feature.Key,
                "5000",
                OverrideType.Permanent
            );

            // Should return subscription override
            var value = await _subscrio.FeatureChecker.GetValueForCustomerAsync<string>(
                customer.Key,
                product.Key,
                feature.Key
            );
            value.Should().Be("5000");
        }

        [Fact]
        public async Task ReturnsNullForNonExistentCustomer()
        {
            var product = await _fixtures.CreateProductAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Null Test Product"
            });

            var value = await _subscrio.FeatureChecker.GetValueForCustomerAsync<string>(
                "non-existent",
                product.Key,
                "any-feature"
            );
            value.Should().BeNull();
        }

        [Fact]
        public async Task ReturnsNullForNonExistentFeature()
        {
            var product = await _fixtures.CreateProductAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Null Feature Product"
            });

            var customer = await _fixtures.CreateCustomerAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Null Test"
            });

            var value = await _subscrio.FeatureChecker.GetValueForCustomerAsync<string>(
                customer.Key,
                product.Key,
                "non-existent-feature"
            );
            value.Should().BeNull();
        }
    }

    public class ResolutionHierarchy : FeatureCheckerTests
    {
        public ResolutionHierarchy() : base() { }

        [Fact]
        public async Task SubscriptionOverrideTakesPrecedenceOverPlanValue()
        {
            var product = await _fixtures.CreateProductAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Hierarchy Product 1"
            });

            var feature = await _fixtures.CreateFeatureAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Projects",
                ["ValueType"] = "numeric",
                ["DefaultValue"] = "3"
            });

            await _subscrio.Products.AssociateFeatureAsync(product.Key, feature.Key);

            var plan = await _fixtures.CreatePlanAsync(product.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Standard"
            });

            var billingCycle = await _fixtures.CreateBillingCycleAsync(plan.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Test Monthly FC",
                ["DurationUnit"] = "months"
            });

            await _subscrio.Plans.SetFeatureValueAsync(plan.Key, feature.Key, "10");

            var customer = await _fixtures.CreateCustomerAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Hierarchy Customer 1"
            });

            var subscription = await _fixtures.CreateSubscriptionAsync(
                customer.Key,
                billingCycle.Key
            );

            // Add override
            await _subscrio.Subscriptions.AddFeatureOverrideAsync(
                subscription.Key,
                feature.Key,
                "25",
                OverrideType.Permanent
            );

            // Should return override (25), not plan (10) or default (3)
            var value = await _subscrio.FeatureChecker.GetValueForCustomerAsync<string>(
                customer.Key,
                product.Key,
                feature.Key
            );
            value.Should().Be("25");
        }

        [Fact]
        public async Task PlanValueTakesPrecedenceOverFeatureDefault()
        {
            var product = await _fixtures.CreateProductAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Hierarchy Product 2"
            });

            var feature = await _fixtures.CreateFeatureAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Storage",
                ["ValueType"] = "numeric",
                ["DefaultValue"] = "5"
            });

            await _subscrio.Products.AssociateFeatureAsync(product.Key, feature.Key);

            var plan = await _fixtures.CreatePlanAsync(product.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Premium"
            });

            var billingCycle = await _fixtures.CreateBillingCycleAsync(plan.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Test Monthly FC",
                ["DurationUnit"] = "months"
            });

            await _subscrio.Plans.SetFeatureValueAsync(plan.Key, feature.Key, "100");

            var customer = await _fixtures.CreateCustomerAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Hierarchy Customer 2"
            });

            var subscription = await _fixtures.CreateSubscriptionAsync(
                customer.Key,
                billingCycle.Key
            );

            // Should return plan value (100), not default (5)
            var value = await _subscrio.FeatureChecker.GetValueForCustomerAsync<string>(
                customer.Key,
                product.Key,
                feature.Key
            );
            value.Should().Be("100");
        }

        [Fact]
        public async Task FallsBackToFeatureDefaultWhenNoPlanValue()
        {
            var product = await _fixtures.CreateProductAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Hierarchy Product 3"
            });

            var feature = await _fixtures.CreateFeatureAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "API Limit",
                ["ValueType"] = "numeric",
                ["DefaultValue"] = "1000"
            });

            await _subscrio.Products.AssociateFeatureAsync(product.Key, feature.Key);

            var plan = await _fixtures.CreatePlanAsync(product.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Basic Plan"
            });

            var billingCycle = await _fixtures.CreateBillingCycleAsync(plan.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Test Monthly FC",
                ["DurationUnit"] = "months"
            });

            // Don't set plan value - should use default

            var customer = await _fixtures.CreateCustomerAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Hierarchy Customer 3"
            });

            var subscription = await _fixtures.CreateSubscriptionAsync(
                customer.Key,
                billingCycle.Key
            );

            // Should return feature default (1000)
            var value = await _subscrio.FeatureChecker.GetValueForCustomerAsync<string>(
                customer.Key,
                product.Key,
                feature.Key
            );
            value.Should().Be("1000");
        }
    }

    public class CompleteResolutionHierarchy : FeatureCheckerTests
    {
        public CompleteResolutionHierarchy() : base() { }

        [Fact]
        public async Task ResolvesFromSubscriptionOverridePlanValueFeatureDefault()
        {
            // 1. Create product
            var product = await _fixtures.CreateProductAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Test Product"
            });

            // 2. Create feature with default value
            var feature = await _fixtures.CreateFeatureAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Max Projects",
                ["ValueType"] = "numeric",
                ["DefaultValue"] = "10"
            });

            // 3. Associate feature with product
            await _subscrio.Products.AssociateFeatureAsync(product.Key, feature.Key);

            // 4. Create plan and set feature value
            var plan = await _fixtures.CreatePlanAsync(product.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Pro Plan"
            });
            await _subscrio.Plans.SetFeatureValueAsync(plan.Key, feature.Key, "50");

            // 5. Create customer
            var customer = await _fixtures.CreateCustomerAsync();

            // 6. Create billing cycle
            var billingCycle = await _fixtures.CreateBillingCycleAsync(plan.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Monthly",
                ["DurationUnit"] = "months"
            });

            // 7. Create subscription
            var subscription = await _fixtures.CreateSubscriptionAsync(
                customer.Key,
                billingCycle.Key
            );

            // TEST: Should resolve from plan value
            var value = await _subscrio.FeatureChecker.GetValueForCustomerAsync<string>(
                customer.Key,
                product.Key,
                feature.Key
            );
            value.Should().Be("50");

            // 8. Add subscription override
            await _subscrio.Subscriptions.AddFeatureOverrideAsync(
                subscription.Key,
                feature.Key,
                "100",
                OverrideType.Permanent
            );

            // TEST: Should now resolve from subscription override
            value = await _subscrio.FeatureChecker.GetValueForCustomerAsync<string>(
                customer.Key,
                product.Key,
                feature.Key
            );
            value.Should().Be("100");

            // 9. Remove override
            await _subscrio.Subscriptions.RemoveFeatureOverrideAsync(subscription.Key, feature.Key);

            // TEST: Should fall back to plan value
            value = await _subscrio.FeatureChecker.GetValueForCustomerAsync<string>(
                customer.Key,
                product.Key,
                feature.Key
            );
            value.Should().Be("50");

            // 10. Remove plan feature value
            await _subscrio.Plans.RemoveFeatureValueAsync(plan.Key, feature.Key);

            // TEST: Should fall back to feature default
            value = await _subscrio.FeatureChecker.GetValueForCustomerAsync<string>(
                customer.Key,
                product.Key,
                feature.Key
            );
            value.Should().Be("10");
        }
    }
}

