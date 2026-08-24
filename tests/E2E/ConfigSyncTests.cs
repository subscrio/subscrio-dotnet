using FluentAssertions;
using Subscrio.Core;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Config;
using Subscrio.Core.Domain.ValueObjects;
using Subscrio.Core.Tests.Setup;
using Xunit;

namespace Subscrio.Core.Tests.E2E;

public class ConfigSyncTests : IDisposable
{
    private readonly Subscrio _subscrio;
    private readonly TestFixtures _fixtures;

    public ConfigSyncTests()
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
    public async Task SyncsConfigurationFromJson()
    {
        var feature1Key = $"max-projects-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var feature2Key = $"gantt-charts-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var productKey = $"project-management-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var planKey = $"basic-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var billingCycleKey = $"monthly-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

        var config = new ConfigSyncDto(
            Version: "1.0",
            Features: new List<FeatureConfig>
            {
                new FeatureConfig(
                    Key: feature1Key,
                    DisplayName: "Maximum Projects",
                    Description: "Maximum number of projects allowed",
                    ValueType: "numeric",
                    DefaultValue: "1",
                    GroupName: "Limits"
                ),
                new FeatureConfig(
                    Key: feature2Key,
                    DisplayName: "Gantt Charts",
                    ValueType: "toggle",
                    DefaultValue: "false",
                    GroupName: "Features"
                )
            },
            Products: new List<ProductConfig>
            {
                new ProductConfig(
                    Key: productKey,
                    DisplayName: "Project Management",
                    Features: new List<string> { feature1Key, feature2Key },
                    Plans: new List<PlanConfig>
                    {
                        new PlanConfig(
                            Key: planKey,
                            DisplayName: "Basic Plan",
                            FeatureValues: new Dictionary<string, string>
                            {
                                [feature1Key] = "5",
                                [feature2Key] = "false"
                            },
                            BillingCycles: new List<BillingCycleConfig>
                            {
                                new BillingCycleConfig(
                                    Key: billingCycleKey,
                                    DisplayName: "Monthly",
                                    DurationValue: 1,
                                    DurationUnit: "months"
                                )
                            }
                        )
                    }
                )
            }
        );

        // Sync configuration
        await _subscrio.ConfigSync.SyncFromJsonAsync(config);

        // Verify features were created
        var feature1 = await _subscrio.Features.GetFeatureAsync(feature1Key);
        feature1.Should().NotBeNull();
        feature1!.DisplayName.Should().Be("Maximum Projects");

        var feature2 = await _subscrio.Features.GetFeatureAsync(feature2Key);
        feature2.Should().NotBeNull();
        feature2!.DisplayName.Should().Be("Gantt Charts");

        // Verify product was created
        var product = await _subscrio.Products.GetProductAsync(productKey);
        product.Should().NotBeNull();
        product!.DisplayName.Should().Be("Project Management");

        // Verify plan was created
        var plan = await _subscrio.Plans.GetPlanAsync(planKey);
        plan.Should().NotBeNull();
        plan!.DisplayName.Should().Be("Basic Plan");

        // Verify feature values were set
        var feature1Value = await _subscrio.Plans.GetFeatureValueAsync(planKey, feature1Key);
        feature1Value.Should().Be("5");

        var feature2Value = await _subscrio.Plans.GetFeatureValueAsync(planKey, feature2Key);
        feature2Value.Should().Be("false");
    }
}

