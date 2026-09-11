using System.Text.Json;
using FluentAssertions;
using Subscrio.Core;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Application.Errors;
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
        var feature1Key = $"max-projects-{Guid.NewGuid():N}";
        var feature2Key = $"gantt-charts-{Guid.NewGuid():N}";
        var productKey = $"project-management-{Guid.NewGuid():N}";
        var planKey = $"basic-{Guid.NewGuid():N}";
        var billingCycleKey = $"monthly-{Guid.NewGuid():N}";

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

    [Fact]
    public async Task SyncFromJsonAsync_UpdatesExistingEntities()
    {
        var featureKey = $"feat-{Guid.NewGuid():N}";
        var productKey = $"prod-{Guid.NewGuid():N}";
        var planKey = $"plan-{Guid.NewGuid():N}";
        var cycleKey = $"cycle-{Guid.NewGuid():N}";

        var initial = BuildMinimalConfig(
            featureKey, productKey, planKey, cycleKey,
            featureDisplayName: "Original Feature",
            featureDefaultValue: "1",
            featureValueType: "numeric",
            productDisplayName: "Original Product",
            planDisplayName: "Original Plan",
            planFeatureValue: "5",
            cycleDisplayName: "Original Cycle");

        var createReport = await _subscrio.ConfigSync.SyncFromJsonAsync(initial);
        createReport.Created.Features.Should().BeGreaterThanOrEqualTo(1);
        createReport.Created.Products.Should().BeGreaterThanOrEqualTo(1);
        createReport.Created.Plans.Should().BeGreaterThanOrEqualTo(1);
        createReport.Created.BillingCycles.Should().BeGreaterThanOrEqualTo(1);

        var updated = BuildMinimalConfig(
            featureKey, productKey, planKey, cycleKey,
            featureDisplayName: "Updated Feature",
            featureDefaultValue: "10",
            featureValueType: "numeric",
            productDisplayName: "Updated Product",
            planDisplayName: "Updated Plan",
            planFeatureValue: "20",
            cycleDisplayName: "Updated Cycle");

        var updateReport = await _subscrio.ConfigSync.SyncFromJsonAsync(updated);

        updateReport.Updated.Features.Should().BeGreaterThan(0);
        updateReport.Updated.Products.Should().BeGreaterThan(0);
        updateReport.Updated.Plans.Should().BeGreaterThan(0);
        updateReport.Updated.BillingCycles.Should().BeGreaterThan(0);

        var feature = await _subscrio.Features.GetFeatureAsync(featureKey);
        feature!.DisplayName.Should().Be("Updated Feature");
        feature.DefaultValue.Should().Be("10");

        var product = await _subscrio.Products.GetProductAsync(productKey);
        product!.DisplayName.Should().Be("Updated Product");

        var plan = await _subscrio.Plans.GetPlanAsync(planKey);
        plan!.DisplayName.Should().Be("Updated Plan");

        var featureValue = await _subscrio.Plans.GetFeatureValueAsync(planKey, featureKey);
        featureValue.Should().Be("20");

        var cycle = await _subscrio.BillingCycles.GetBillingCycleAsync(cycleKey);
        cycle!.DisplayName.Should().Be("Updated Cycle");
    }

    [Fact]
    public async Task SyncFromJsonAsync_ReportsIgnoredEntities()
    {
        var ignoredProduct = await _fixtures.CreateProductAsync(new Dictionary<string, object>
        {
            ["Key"] = $"ignored-prod-{Guid.NewGuid():N}",
            ["DisplayName"] = "Ignored Product"
        });

        var featureKey = $"feat-{Guid.NewGuid():N}";
        var productKey = $"prod-{Guid.NewGuid():N}";
        var planKey = $"plan-{Guid.NewGuid():N}";
        var cycleKey = $"cycle-{Guid.NewGuid():N}";

        var config = BuildMinimalConfig(featureKey, productKey, planKey, cycleKey);
        var report = await _subscrio.ConfigSync.SyncFromJsonAsync(config);

        report.Ignored.Products.Should().BeGreaterThanOrEqualTo(1);
        ignoredProduct.Key.Should().NotBe(productKey);
    }

    [Fact]
    public async Task SyncFromJsonAsync_ClearsOnExpireTransitionWhenOmitted()
    {
        var featureKey = $"feat-{Guid.NewGuid():N}";
        var productKey = $"prod-{Guid.NewGuid():N}";
        var planKey = $"plan-{Guid.NewGuid():N}";
        var freePlanKey = $"free-plan-{Guid.NewGuid():N}";
        var cycleKey = $"cycle-{Guid.NewGuid():N}";
        var freeCycleKey = $"free-cycle-{Guid.NewGuid():N}";

        var withTransition = new ConfigSyncDto(
            Version: "1.0",
            Features: new List<FeatureConfig>
            {
                new FeatureConfig(
                    Key: featureKey,
                    DisplayName: "Feature",
                    ValueType: "toggle",
                    DefaultValue: "false")
            },
            Products: new List<ProductConfig>
            {
                new ProductConfig(
                    Key: productKey,
                    DisplayName: "Product",
                    Features: new List<string> { featureKey },
                    Plans: new List<PlanConfig>
                    {
                        new PlanConfig(
                            Key: planKey,
                            DisplayName: "Paid Plan",
                            OnExpireTransitionToBillingCycleKey: freeCycleKey,
                            FeatureValues: new Dictionary<string, string> { [featureKey] = "true" },
                            BillingCycles: new List<BillingCycleConfig>
                            {
                                new BillingCycleConfig(
                                    Key: cycleKey,
                                    DisplayName: "Monthly",
                                    DurationValue: 1,
                                    DurationUnit: "months")
                            }),
                        new PlanConfig(
                            Key: freePlanKey,
                            DisplayName: "Free Plan",
                            FeatureValues: new Dictionary<string, string> { [featureKey] = "false" },
                            BillingCycles: new List<BillingCycleConfig>
                            {
                                new BillingCycleConfig(
                                    Key: freeCycleKey,
                                    DisplayName: "Forever",
                                    DurationUnit: "forever")
                            })
                    })
            });

        await _subscrio.ConfigSync.SyncFromJsonAsync(withTransition);

        var planAfterCreate = await _subscrio.Plans.GetPlanAsync(planKey);
        planAfterCreate!.OnExpireTransitionToBillingCycleKey.Should().Be(freeCycleKey);

        var withoutTransition = new ConfigSyncDto(
            Version: "1.0",
            Features: new List<FeatureConfig>
            {
                new FeatureConfig(
                    Key: featureKey,
                    DisplayName: "Feature",
                    ValueType: "toggle",
                    DefaultValue: "false")
            },
            Products: new List<ProductConfig>
            {
                new ProductConfig(
                    Key: productKey,
                    DisplayName: "Product",
                    Features: new List<string> { featureKey },
                    Plans: new List<PlanConfig>
                    {
                        new PlanConfig(
                            Key: planKey,
                            DisplayName: "Paid Plan",
                            FeatureValues: new Dictionary<string, string> { [featureKey] = "true" },
                            BillingCycles: new List<BillingCycleConfig>
                            {
                                new BillingCycleConfig(
                                    Key: cycleKey,
                                    DisplayName: "Monthly",
                                    DurationValue: 1,
                                    DurationUnit: "months")
                            }),
                        new PlanConfig(
                            Key: freePlanKey,
                            DisplayName: "Free Plan",
                            FeatureValues: new Dictionary<string, string> { [featureKey] = "false" },
                            BillingCycles: new List<BillingCycleConfig>
                            {
                                new BillingCycleConfig(
                                    Key: freeCycleKey,
                                    DisplayName: "Forever",
                                    DurationUnit: "forever")
                            })
                    })
            });

        await _subscrio.ConfigSync.SyncFromJsonAsync(withoutTransition);

        var planAfterClear = await _subscrio.Plans.GetPlanAsync(planKey);
        planAfterClear!.OnExpireTransitionToBillingCycleKey.Should().BeNull();
    }

    [Fact]
    public async Task SyncFromFileAsync_LoadsValidJson()
    {
        var featureKey = $"feat-{Guid.NewGuid():N}";
        var productKey = $"prod-{Guid.NewGuid():N}";
        var planKey = $"plan-{Guid.NewGuid():N}";
        var cycleKey = $"cycle-{Guid.NewGuid():N}";

        var config = BuildMinimalConfig(featureKey, productKey, planKey, cycleKey);
        var filePath = Path.Combine(Path.GetTempPath(), $"subscrio-config-{Guid.NewGuid():N}.json");

        try
        {
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });
            await File.WriteAllTextAsync(filePath, json);

            var report = await _subscrio.ConfigSync.SyncFromFileAsync(filePath);

            report.Errors.Should().BeEmpty();
            report.Created.Features.Should().BeGreaterThanOrEqualTo(1);
            report.Created.Products.Should().BeGreaterThanOrEqualTo(1);

            var product = await _subscrio.Products.GetProductAsync(productKey);
            product.Should().NotBeNull();
            product!.DisplayName.Should().Be("Original Product");
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Fact]
    public async Task SyncFromFileAsync_ThrowsOnMissingFile()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"missing-config-{Guid.NewGuid():N}.json");

        var act = async () => await _subscrio.ConfigSync.SyncFromFileAsync(missingPath);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*Failed to load configuration from file*");
    }

    [Fact]
    public async Task SyncFromJsonAsync_UpdatesFeatureValueType()
    {
        var featureKey = $"feat-type-{Guid.NewGuid():N}";
        var productKey = $"prod-{Guid.NewGuid():N}";
        var planKey = $"plan-{Guid.NewGuid():N}";
        var cycleKey = $"cycle-{Guid.NewGuid():N}";

        var numeric = BuildMinimalConfig(
            featureKey, productKey, planKey, cycleKey,
            featureDisplayName: "Typed Feature",
            featureDefaultValue: "42",
            featureValueType: "numeric",
            planFeatureValue: "42");

        await _subscrio.ConfigSync.SyncFromJsonAsync(numeric);

        var before = await _subscrio.Features.GetFeatureAsync(featureKey);
        before!.ValueType.Should().Be("numeric");

        var asText = BuildMinimalConfig(
            featureKey, productKey, planKey, cycleKey,
            featureDisplayName: "Typed Feature",
            featureDefaultValue: "hello",
            featureValueType: "text",
            planFeatureValue: "hello");

        var report = await _subscrio.ConfigSync.SyncFromJsonAsync(asText);
        report.Updated.Features.Should().BeGreaterThan(0);

        var after = await _subscrio.Features.GetFeatureAsync(featureKey);
        after!.ValueType.Should().Be("text");
        after.DefaultValue.Should().Be("hello");
    }

    private static ConfigSyncDto BuildMinimalConfig(
        string featureKey,
        string productKey,
        string planKey,
        string cycleKey,
        string featureDisplayName = "Original Feature",
        string featureDefaultValue = "1",
        string featureValueType = "numeric",
        string productDisplayName = "Original Product",
        string planDisplayName = "Original Plan",
        string planFeatureValue = "5",
        string cycleDisplayName = "Original Cycle")
    {
        return new ConfigSyncDto(
            Version: "1.0",
            Features: new List<FeatureConfig>
            {
                new FeatureConfig(
                    Key: featureKey,
                    DisplayName: featureDisplayName,
                    ValueType: featureValueType,
                    DefaultValue: featureDefaultValue,
                    GroupName: "Limits")
            },
            Products: new List<ProductConfig>
            {
                new ProductConfig(
                    Key: productKey,
                    DisplayName: productDisplayName,
                    Features: new List<string> { featureKey },
                    Plans: new List<PlanConfig>
                    {
                        new PlanConfig(
                            Key: planKey,
                            DisplayName: planDisplayName,
                            FeatureValues: new Dictionary<string, string>
                            {
                                [featureKey] = planFeatureValue
                            },
                            BillingCycles: new List<BillingCycleConfig>
                            {
                                new BillingCycleConfig(
                                    Key: cycleKey,
                                    DisplayName: cycleDisplayName,
                                    DurationValue: 1,
                                    DurationUnit: "months")
                            })
                    })
            });
    }
}
