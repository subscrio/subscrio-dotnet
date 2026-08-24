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

public class BillingCyclesTests : IDisposable
{
    private readonly Subscrio _subscrio;
    private readonly TestFixtures _fixtures;

    public BillingCyclesTests()
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

    public class CrudOperations : BillingCyclesTests
    {
        public CrudOperations() : base() { }

        [Fact]
        public async Task CreatesBillingCycleDays()
        {
            var product = await _fixtures.CreateProductAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Billing Test Product"
            });

            var plan = await _fixtures.CreatePlanAsync(product.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Billing Test Plan"
            });

            var cycle = await _fixtures.CreateBillingCycleAsync(plan.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Daily Cycle",
                ["DurationValue"] = 1,
                ["DurationUnit"] = "days"
            });

            cycle.Should().NotBeNull();
            cycle.ProductKey.Should().Be(product.Key);
            cycle.PlanKey.Should().Be(plan.Key);
            cycle.Key.Should().NotBeNullOrEmpty();
            cycle.DurationValue.Should().Be(1);
            cycle.DurationUnit.Should().Be("days");
        }

        [Fact]
        public async Task CreatesBillingCycleWeeks()
        {
            var product = await _fixtures.CreateProductAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Billing Weeks Product"
            });

            var plan = await _fixtures.CreatePlanAsync(product.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Billing Weeks Plan"
            });

            var cycle = await _fixtures.CreateBillingCycleAsync(plan.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Weekly Cycle",
                ["DurationValue"] = 1,
                ["DurationUnit"] = "weeks"
            });

            cycle.DurationUnit.Should().Be("weeks");
        }

        [Fact]
        public async Task CreatesBillingCycleMonths()
        {
            var product = await _fixtures.CreateProductAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Billing Months Product"
            });

            var plan = await _fixtures.CreatePlanAsync(product.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Billing Months Plan"
            });

            var cycle = await _fixtures.CreateBillingCycleAsync(plan.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Monthly Cycle",
                ["DurationValue"] = 1,
                ["DurationUnit"] = "months"
            });

            cycle.DurationUnit.Should().Be("months");
        }

        [Fact]
        public async Task CreatesBillingCycleYears()
        {
            var product = await _fixtures.CreateProductAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Billing Years Product"
            });

            var plan = await _fixtures.CreatePlanAsync(product.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Billing Years Plan"
            });

            var cycle = await _fixtures.CreateBillingCycleAsync(plan.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Yearly Cycle",
                ["DurationValue"] = 1,
                ["DurationUnit"] = "years"
            });

            cycle.DurationUnit.Should().Be("years");
        }

        [Fact]
        public async Task CreatesBillingCycleWithExternalProductId()
        {
            var product = await _fixtures.CreateProductAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "External Product"
            });

            var plan = await _fixtures.CreatePlanAsync(product.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "External Plan"
            });

            var cycle = await _fixtures.CreateBillingCycleAsync(plan.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Stripe Monthly",
                ["DurationValue"] = 1,
                ["DurationUnit"] = "months",
                ["ExternalProductId"] = "price_1234567890"
            });

            cycle.ExternalProductId.Should().Be("price_1234567890");
        }

        [Fact]
        public async Task RetrievesBillingCycleByKey()
        {
            var product = await _fixtures.CreateProductAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Retrieve Cycle Product"
            });

            var plan = await _fixtures.CreatePlanAsync(product.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Retrieve Cycle Plan"
            });

            var created = await _fixtures.CreateBillingCycleAsync(plan.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Retrieve Cycle",
                ["DurationValue"] = 1,
                ["DurationUnit"] = "months"
            });

            var retrieved = await _subscrio.BillingCycles.GetBillingCycleAsync(created.Key);
            retrieved.Should().NotBeNull();
            retrieved!.Key.Should().Be(created.Key);
        }

        [Fact]
        public async Task UpdatesBillingCycleDisplayName()
        {
            var product = await _fixtures.CreateProductAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Update Name Product"
            });

            var plan = await _fixtures.CreatePlanAsync(product.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Update Name Plan"
            });

            var cycle = await _fixtures.CreateBillingCycleAsync(plan.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Original Name",
                ["DurationValue"] = 1,
                ["DurationUnit"] = "months"
            });

            var updated = await _subscrio.BillingCycles.UpdateBillingCycleAsync(cycle.Key, new UpdateBillingCycleDto(
                DisplayName: "Updated Name"
            ));

            updated.DisplayName.Should().Be("Updated Name");
        }

        [Fact]
        public async Task UpdatesBillingCycleDuration()
        {
            var product = await _fixtures.CreateProductAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Update Duration Product"
            });

            var plan = await _fixtures.CreatePlanAsync(product.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Update Duration Plan"
            });

            var cycle = await _fixtures.CreateBillingCycleAsync(plan.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Update Duration",
                ["DurationValue"] = 1,
                ["DurationUnit"] = "months"
            });

            var updated = await _subscrio.BillingCycles.UpdateBillingCycleAsync(cycle.Key, new UpdateBillingCycleDto(
                DurationValue: 3
            ));

            updated.DurationValue.Should().Be(3);
        }

        [Fact]
        public async Task ReturnsNullForNonExistentBillingCycle()
        {
            var result = await _subscrio.BillingCycles.GetBillingCycleAsync("non-existent-cycle");
            result.Should().BeNull();
        }
    }

    public class Validation : BillingCyclesTests
    {
        public Validation() : base() { }

        [Fact]
        public async Task ThrowsErrorForDuplicateBillingCycleKey()
        {
            var product = await _fixtures.CreateProductAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Duplicate Cycle Product"
            });

            var plan = await _fixtures.CreatePlanAsync(product.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Duplicate Cycle Plan"
            });

            // Use explicit key for duplicate test scenario
            await _fixtures.CreateBillingCycleAsync(plan.Key, new Dictionary<string, object>
            {
                ["Key"] = "duplicate-cycle",
                ["DisplayName"] = "Cycle 1",
                ["DurationValue"] = 1,
                ["DurationUnit"] = "months"
            });

            await Assert.ThrowsAsync<ConflictException>(async () =>
            {
                await _fixtures.CreateBillingCycleAsync(plan.Key, new Dictionary<string, object>
                {
                    ["Key"] = "duplicate-cycle",
                    ["DisplayName"] = "Cycle 2",
                    ["DurationValue"] = 1,
                    ["DurationUnit"] = "months"
                });
            });
        }

        [Fact]
        public async Task ThrowsErrorForNonExistentPlanKey()
        {
            await Assert.ThrowsAsync<NotFoundException>(async () =>
            {
                await _subscrio.BillingCycles.CreateBillingCycleAsync(new CreateBillingCycleDto(
                    PlanKey: "non-existent-plan",
                    Key: "test-cycle",
                    DisplayName: "Test Cycle",
                    DurationValue: 1,
                    DurationUnit: "months"
                ));
            });
        }
    }

    public class Lifecycle : BillingCyclesTests
    {
        public Lifecycle() : base() { }

        [Fact]
        public async Task ArchivesAndUnarchivesBillingCycle()
        {
            var product = await _fixtures.CreateProductAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Lifecycle Product"
            });

            var plan = await _fixtures.CreatePlanAsync(product.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Lifecycle Plan"
            });

            var cycle = await _fixtures.CreateBillingCycleAsync(plan.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Lifecycle Cycle",
                ["DurationValue"] = 1,
                ["DurationUnit"] = "months"
            });

            await _subscrio.BillingCycles.ArchiveBillingCycleAsync(cycle.Key);
            var archived = await _subscrio.BillingCycles.GetBillingCycleAsync(cycle.Key);
            archived!.Status.Should().Be("archived");

            await _subscrio.BillingCycles.UnarchiveBillingCycleAsync(cycle.Key);
            var unarchived = await _subscrio.BillingCycles.GetBillingCycleAsync(cycle.Key);
            unarchived!.Status.Should().Be("active");
        }

        [Fact]
        public async Task DeletesArchivedBillingCycle()
        {
            var product = await _fixtures.CreateProductAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Delete Cycle Product"
            });

            var plan = await _fixtures.CreatePlanAsync(product.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Delete Cycle Plan"
            });

            var cycle = await _fixtures.CreateBillingCycleAsync(plan.Key, new Dictionary<string, object>
            {
                ["DisplayName"] = "Delete Cycle",
                ["DurationValue"] = 1,
                ["DurationUnit"] = "months"
            });

            await _subscrio.BillingCycles.ArchiveBillingCycleAsync(cycle.Key);
            await _subscrio.BillingCycles.DeleteBillingCycleAsync(cycle.Key);

            var result = await _subscrio.BillingCycles.GetBillingCycleAsync(cycle.Key);
            result.Should().BeNull();
        }
    }
}


