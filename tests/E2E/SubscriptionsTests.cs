using FluentAssertions;
using Subscrio.Core;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Application.Errors;
using Subscrio.Core.Config;
using Subscrio.Core.Domain.ValueObjects;
using Subscrio.Core.Tests.Setup;
using System.Collections.Generic;
using Xunit;

namespace Subscrio.Core.Tests.E2E;

public class SubscriptionsTests : IDisposable
{
    private readonly Subscrio _subscrio;
    private readonly TestFixtures _fixtures;

    public SubscriptionsTests()
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

    public class CrudOperations : SubscriptionsTests
    {
        public CrudOperations() : base() { }

        [Fact]
        public async Task CreatesSubscriptionWithValidData()
        {
            var customer = await _fixtures.CreateCustomerAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Sub Customer 1"
            });

            var product = await _fixtures.CreateProductAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Sub Product 1"
            });

            var plan = await _fixtures.CreatePlanAsync(product.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Sub Plan 1"
            });

            var billingCycle = await _fixtures.CreateBillingCycleAsync(plan.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Test Monthly",
                ["DurationValue"] = 1,
                ["DurationUnit"] = "months"
            });

            var subscription = await _fixtures.CreateSubscriptionAsync(
                customer.Key,
                billingCycle.Key
            );

            subscription.Should().NotBeNull();
            subscription.Key.Should().NotBeNullOrEmpty();
            subscription.CustomerKey.Should().Be(customer.Key);
            subscription.ProductKey.Should().Be(product.Key);
            subscription.PlanKey.Should().Be(plan.Key);
            subscription.BillingCycleKey.Should().Be(billingCycle.Key);
            subscription.Status.Should().Be("active");
        }

        [Fact]
        public async Task CreatesSubscriptionWithTrialPeriod()
        {
            var customer = await _fixtures.CreateCustomerAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Trial Customer"
            });

            var product = await _fixtures.CreateProductAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Trial Product"
            });

            var plan = await _fixtures.CreatePlanAsync(product.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Trial Plan"
            });

            var billingCycle = await _fixtures.CreateBillingCycleAsync(plan.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Test Monthly",
                ["DurationValue"] = 1,
                ["DurationUnit"] = "months"
            });

            var trialEnd = DateTime.UtcNow.AddDays(14);

            var subscription = await _fixtures.CreateSubscriptionAsync(
                customer.Key,
                billingCycle.Key,
                new Dictionary<string, object>
                {
                    ["TrialEndDate"] = trialEnd
                }
            );

            subscription.TrialEndDate.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task RetrievesSubscriptionByKey()
        {
            var customer = await _fixtures.CreateCustomerAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Retrieve Sub Customer"
            });

            var product = await _fixtures.CreateProductAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Retrieve Sub Product"
            });

            var plan = await _fixtures.CreatePlanAsync(product.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Retrieve Sub Plan"
            });

            var billingCycle = await _fixtures.CreateBillingCycleAsync(plan.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Test Monthly",
                ["DurationValue"] = 1,
                ["DurationUnit"] = "months"
            });

            var created = await _fixtures.CreateSubscriptionAsync(
                customer.Key,
                billingCycle.Key
            );

            var retrieved = await _subscrio.Subscriptions.GetSubscriptionAsync(created.Key);
            retrieved.Should().NotBeNull();
            retrieved!.Key.Should().Be(created.Key);
        }

        [Fact]
        public async Task UpdatesSubscriptionMetadata()
        {
            var customer = await _fixtures.CreateCustomerAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Update Sub Customer"
            });

            var product = await _fixtures.CreateProductAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Update Sub Product"
            });

            var plan = await _fixtures.CreatePlanAsync(product.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Update Sub Plan"
            });

            var billingCycle = await _fixtures.CreateBillingCycleAsync(plan.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Test Monthly",
                ["DurationValue"] = 1,
                ["DurationUnit"] = "months"
            });

            var subscription = await _fixtures.CreateSubscriptionAsync(
                customer.Key,
                billingCycle.Key
            );

            var updated = await _subscrio.Subscriptions.UpdateSubscriptionAsync(subscription.Key, new UpdateSubscriptionDto(
                Metadata: new Dictionary<string, object?> { ["updated"] = true }
            ));

            updated.Metadata.Should().ContainKey("updated");
        }

        [Fact]
        public async Task ReturnsNullForNonExistentSubscription()
        {
            var result = await _subscrio.Subscriptions.GetSubscriptionAsync("non-existent-subscription");
            result.Should().BeNull();
        }
    }

    public class FeatureOverrides : SubscriptionsTests
    {
        public FeatureOverrides() : base() { }

        [Fact]
        public async Task AddsAndRemovesFeatureOverride()
        {
            var product = await _fixtures.CreateProductAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Override Product"
            });

            var feature = await _fixtures.CreateFeatureAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Override Feature",
                ["ValueType"] = "numeric",
                ["DefaultValue"] = "10"
            });

            await _subscrio.Products.AssociateFeatureAsync(product.Key, feature.Key);

            var plan = await _fixtures.CreatePlanAsync(product.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Override Plan"
            });

            var billingCycle = await _fixtures.CreateBillingCycleAsync(plan.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Test Monthly",
                ["DurationValue"] = 1,
                ["DurationUnit"] = "months"
            });

            var customer = await _fixtures.CreateCustomerAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Override Customer"
            });

            var subscription = await _fixtures.CreateSubscriptionAsync(
                customer.Key,
                billingCycle.Key
            );

            // Add override
            await _subscrio.Subscriptions.AddFeatureOverrideAsync(
                subscription.Key,
                feature.Key,
                "100",
                OverrideType.Permanent
            );

            // Verify override is applied
            var value = await _subscrio.FeatureChecker.GetValueForCustomerAsync<string>(
                customer.Key,
                product.Key,
                feature.Key
            );
            value.Should().Be("100");

            // Remove override
            await _subscrio.Subscriptions.RemoveFeatureOverrideAsync(subscription.Key, feature.Key);

            // Should fall back to default
            value = await _subscrio.FeatureChecker.GetValueForCustomerAsync<string>(
                customer.Key,
                product.Key,
                feature.Key
            );
            value.Should().Be("10");
        }

        [Fact]
        public async Task ClearsTemporaryOverrides()
        {
            var product = await _fixtures.CreateProductAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Temp Override Product"
            });

            var feature = await _fixtures.CreateFeatureAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Temp Override Feature",
                ["ValueType"] = "numeric",
                ["DefaultValue"] = "10"
            });

            await _subscrio.Products.AssociateFeatureAsync(product.Key, feature.Key);

            var plan = await _fixtures.CreatePlanAsync(product.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Temp Override Plan"
            });

            var billingCycle = await _fixtures.CreateBillingCycleAsync(plan.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Test Monthly",
                ["DurationValue"] = 1,
                ["DurationUnit"] = "months"
            });

            var customer = await _fixtures.CreateCustomerAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Temp Override Customer"
            });

            var subscription = await _fixtures.CreateSubscriptionAsync(
                customer.Key,
                billingCycle.Key
            );

            // Add temporary override
            await _subscrio.Subscriptions.AddFeatureOverrideAsync(
                subscription.Key,
                feature.Key,
                "50",
                OverrideType.Temporary
            );

            // Clear temporary overrides
            await _subscrio.Subscriptions.ClearTemporaryOverridesAsync(subscription.Key);

            // Should fall back to default
            var value = await _subscrio.FeatureChecker.GetValueForCustomerAsync<string>(
                customer.Key,
                product.Key,
                feature.Key
            );
            value.Should().Be("10");
        }
    }

    public class Lifecycle : SubscriptionsTests
    {
        public Lifecycle() : base() { }

        [Fact]
        public async Task ArchivesAndUnarchivesSubscription()
        {
            var customer = await _fixtures.CreateCustomerAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Archive Sub Customer"
            });

            var product = await _fixtures.CreateProductAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Archive Sub Product"
            });

            var plan = await _fixtures.CreatePlanAsync(product.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Archive Sub Plan"
            });

            var billingCycle = await _fixtures.CreateBillingCycleAsync(plan.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Test Monthly",
                ["DurationValue"] = 1,
                ["DurationUnit"] = "months"
            });

            var subscription = await _fixtures.CreateSubscriptionAsync(
                customer.Key,
                billingCycle.Key
            );

            await _subscrio.Subscriptions.ArchiveSubscriptionAsync(subscription.Key);
            var archived = await _subscrio.Subscriptions.GetSubscriptionAsync(subscription.Key);
            archived!.IsArchived.Should().BeTrue();

            await _subscrio.Subscriptions.UnarchiveSubscriptionAsync(subscription.Key);
            var unarchived = await _subscrio.Subscriptions.GetSubscriptionAsync(subscription.Key);
            unarchived!.IsArchived.Should().BeFalse();
        }
    }
}

