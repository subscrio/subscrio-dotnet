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

public class FeaturesTests : IDisposable
{
    private readonly Subscrio _subscrio;
    private readonly TestFixtures _fixtures;

    public FeaturesTests()
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

    public class CrudOperations : FeaturesTests
    {
        public CrudOperations() : base() { }

        [Fact]
        public async Task CreatesFeatureWithValidDataToggleType()
        {
            var feature = await _fixtures.CreateFeatureAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Toggle Feature",
                ["Description"] = "A toggle feature",
                ["ValueType"] = "toggle",
                ["DefaultValue"] = "false"
            });

            feature.Should().NotBeNull();
            feature.Key.Should().NotBeNullOrEmpty();
            feature.DisplayName.Should().Be("Toggle Feature");
            feature.ValueType.Should().Be("toggle");
            feature.DefaultValue.Should().Be("false");
            feature.Status.Should().Be("active");
        }

        [Fact]
        public async Task CreatesFeatureWithValidDataNumericType()
        {
            var feature = await _fixtures.CreateFeatureAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Numeric Feature",
                ["ValueType"] = "numeric",
                ["DefaultValue"] = "100"
            });

            feature.ValueType.Should().Be("numeric");
            feature.DefaultValue.Should().Be("100");
        }

        [Fact]
        public async Task CreatesFeatureWithValidDataTextType()
        {
            var feature = await _fixtures.CreateFeatureAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Text Feature",
                ["ValueType"] = "text",
                ["DefaultValue"] = "default-text"
            });

            feature.ValueType.Should().Be("text");
            feature.DefaultValue.Should().Be("default-text");
        }

        [Fact]
        public async Task RetrievesFeatureByKeyAfterCreation()
        {
            var created = await _fixtures.CreateFeatureAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Retrieve Feature",
                ["ValueType"] = "toggle",
                ["DefaultValue"] = "false"
            });

            var retrieved = await _subscrio.Features.GetFeatureAsync(created.Key);
            retrieved.Should().NotBeNull();
            retrieved!.Key.Should().Be(created.Key);
            retrieved.DisplayName.Should().Be(created.DisplayName);
        }

        [Fact]
        public async Task UpdatesFeatureDisplayName()
        {
            var feature = await _fixtures.CreateFeatureAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Original Name",
                ["ValueType"] = "toggle",
                ["DefaultValue"] = "false"
            });

            var updated = await _subscrio.Features.UpdateFeatureAsync(feature.Key, new UpdateFeatureDto(
                DisplayName: "Updated Name"
            ));

            updated.DisplayName.Should().Be("Updated Name");
        }

        [Fact]
        public async Task UpdatesFeatureDefaultValue()
        {
            var feature = await _fixtures.CreateFeatureAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Update Value",
                ["ValueType"] = "numeric",
                ["DefaultValue"] = "10"
            });

            var updated = await _subscrio.Features.UpdateFeatureAsync(feature.Key, new UpdateFeatureDto(
                DefaultValue: "20"
            ));

            updated.DefaultValue.Should().Be("20");
        }

        [Fact]
        public async Task UpdatesFeatureDescription()
        {
            var feature = await _fixtures.CreateFeatureAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Update Desc",
                ["ValueType"] = "toggle",
                ["DefaultValue"] = "false"
            });

            var updated = await _subscrio.Features.UpdateFeatureAsync(feature.Key, new UpdateFeatureDto(
                Description: "Updated description"
            ));

            updated.Description.Should().Be("Updated description");
        }

        [Fact]
        public async Task ReturnsNullForNonExistentFeature()
        {
            var result = await _subscrio.Features.GetFeatureAsync("non-existent-feature");
            result.Should().BeNull();
        }

        [Fact]
        public async Task ThrowsErrorWhenUpdatingNonExistentFeature()
        {
            await Assert.ThrowsAsync<NotFoundException>(async () =>
            {
                await _subscrio.Features.UpdateFeatureAsync("non-existent-feature", new UpdateFeatureDto(
                    DisplayName: "New Name"
                ));
            });
        }
    }

    public class FeatureValidation : FeaturesTests
    {
        public FeatureValidation() : base() { }

        [Fact]
        public async Task ThrowsErrorForDuplicateFeatureKey()
        {
            // Use explicit key for duplicate test scenario
            await _fixtures.CreateFeatureAsync(new Dictionary<string, object>
            {
                ["Key"] = "duplicate-feature",
                ["DisplayName"] = "Feature 1",
                ["ValueType"] = "toggle",
                ["DefaultValue"] = "false"
            });

            await Assert.ThrowsAsync<ConflictException>(async () =>
            {
                await _fixtures.CreateFeatureAsync(new Dictionary<string, object>
                {
                    ["Key"] = "duplicate-feature",
                    ["DisplayName"] = "Feature 2",
                    ["ValueType"] = "toggle",
                    ["DefaultValue"] = "false"
                });
            });
        }

        [Fact]
        public async Task ValidatesFeatureKeyFormat()
        {
            await Assert.ThrowsAsync<ValidationException>(async () =>
            {
                await _subscrio.Features.CreateFeatureAsync(new CreateFeatureDto(
                    Key: "INVALID KEY!",
                    DisplayName: "Invalid Feature",
                    ValueType: "toggle",
                    DefaultValue: "false"
                ));
            });
        }

        [Fact]
        public async Task ValidatesValueType()
        {
            await Assert.ThrowsAsync<ValidationException>(async () =>
            {
                await _subscrio.Features.CreateFeatureAsync(new CreateFeatureDto(
                    Key: "invalid-type-feature",
                    DisplayName: "Invalid Type",
                    ValueType: "invalid-type",
                    DefaultValue: "false"
                ));
            });
        }

        [Fact]
        public async Task ValidatesDefaultValueForToggleType()
        {
            await Assert.ThrowsAsync<ValidationException>(async () =>
            {
                await _subscrio.Features.CreateFeatureAsync(new CreateFeatureDto(
                    Key: "invalid-toggle-feature",
                    DisplayName: "Invalid Toggle",
                    ValueType: "toggle",
                    DefaultValue: "invalid"
                ));
            });
        }

        [Fact]
        public async Task ValidatesDefaultValueForNumericType()
        {
            await Assert.ThrowsAsync<ValidationException>(async () =>
            {
                await _subscrio.Features.CreateFeatureAsync(new CreateFeatureDto(
                    Key: "invalid-numeric-feature",
                    DisplayName: "Invalid Numeric",
                    ValueType: "numeric",
                    DefaultValue: "not-a-number"
                ));
            });
        }
    }

    public class FeatureLifecycle : FeaturesTests
    {
        public FeatureLifecycle() : base() { }

        [Fact]
        public async Task ArchivesAndUnarchivesFeature()
        {
            var feature = await _fixtures.CreateFeatureAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Lifecycle Feature",
                ["ValueType"] = "toggle",
                ["DefaultValue"] = "false"
            });

            await _subscrio.Features.ArchiveFeatureAsync(feature.Key);
            var archived = await _subscrio.Features.GetFeatureAsync(feature.Key);
            archived!.Status.Should().Be("archived");

            await _subscrio.Features.UnarchiveFeatureAsync(feature.Key);
            var unarchived = await _subscrio.Features.GetFeatureAsync(feature.Key);
            unarchived!.Status.Should().Be("active");
        }

        [Fact]
        public async Task DeletesArchivedFeature()
        {
            var feature = await _fixtures.CreateFeatureAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Delete Feature",
                ["ValueType"] = "toggle",
                ["DefaultValue"] = "false"
            });

            await _subscrio.Features.ArchiveFeatureAsync(feature.Key);
            await _subscrio.Features.DeleteFeatureAsync(feature.Key);

            var result = await _subscrio.Features.GetFeatureAsync(feature.Key);
            result.Should().BeNull();
        }

        [Fact]
        public async Task PreventsDeletionOfActiveFeature()
        {
            var feature = await _fixtures.CreateFeatureAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "No Delete",
                ["ValueType"] = "toggle",
                ["DefaultValue"] = "false"
            });

            await Assert.ThrowsAsync<DomainException>(async () =>
            {
                await _subscrio.Features.DeleteFeatureAsync(feature.Key);
            });
        }
    }

    public class FeatureListing : FeaturesTests
    {
        public FeatureListing() : base() { }

        [Fact]
        public async Task ListsAllFeatures()
        {
            await _fixtures.CreateFeatureAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "List 1",
                ["ValueType"] = "toggle",
                ["DefaultValue"] = "false"
            });
            await _fixtures.CreateFeatureAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "List 2",
                ["ValueType"] = "toggle",
                ["DefaultValue"] = "false"
            });

            var features = await _subscrio.Features.ListFeaturesAsync();
            features.Should().HaveCountGreaterThanOrEqualTo(2);
        }

        [Fact]
        public async Task FiltersFeaturesByStatus()
        {
            var active = await _fixtures.CreateFeatureAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Filter Active",
                ["ValueType"] = "toggle",
                ["DefaultValue"] = "false"
            });
            var archived = await _fixtures.CreateFeatureAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Filter Archived",
                ["ValueType"] = "toggle",
                ["DefaultValue"] = "false"
            });
            await _subscrio.Features.ArchiveFeatureAsync(archived.Key);

            var activeFeatures = await _subscrio.Features.ListFeaturesAsync(new FeatureFilterDto(Status: "active"));
            activeFeatures.Should().AllSatisfy(f => f.Status.Should().Be("active"));

            var archivedFeatures = await _subscrio.Features.ListFeaturesAsync(new FeatureFilterDto(Status: "archived"));
            archivedFeatures.Should().Contain(f => f.Key == archived.Key);
        }

        [Fact]
        public async Task FiltersFeaturesByValueType()
        {
            await _fixtures.CreateFeatureAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Filter Toggle",
                ["ValueType"] = "toggle",
                ["DefaultValue"] = "false"
            });
            await _fixtures.CreateFeatureAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Filter Numeric",
                ["ValueType"] = "numeric",
                ["DefaultValue"] = "100"
            });

            var toggleFeatures = await _subscrio.Features.ListFeaturesAsync(new FeatureFilterDto(ValueType: "toggle"));
            toggleFeatures.Should().AllSatisfy(f => f.ValueType.Should().Be("toggle"));
        }
    }

    public class FeatureProductAssociation : FeaturesTests
    {
        public FeatureProductAssociation() : base() { }

        [Fact]
        public async Task GetsFeaturesByProduct()
        {
            var product = await _fixtures.CreateProductAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Product For Features"
            });

            var feature1 = await _fixtures.CreateFeatureAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Product Feature 1",
                ["ValueType"] = "toggle",
                ["DefaultValue"] = "false"
            });
            var feature2 = await _fixtures.CreateFeatureAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Product Feature 2",
                ["ValueType"] = "toggle",
                ["DefaultValue"] = "false"
            });

            await _subscrio.Products.AssociateFeatureAsync(product.Key, feature1.Key);
            await _subscrio.Products.AssociateFeatureAsync(product.Key, feature2.Key);

            var features = await _subscrio.Features.GetFeaturesByProductAsync(product.Key);
            features.Should().HaveCount(2);
            features.Should().Contain(f => f.Key == feature1.Key);
            features.Should().Contain(f => f.Key == feature2.Key);
        }

        [Fact]
        public async Task ReturnsEmptyListForProductWithNoFeatures()
        {
            var product = await _fixtures.CreateProductAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Product No Features"
            });

            var features = await _subscrio.Features.GetFeaturesByProductAsync(product.Key);
            features.Should().BeEmpty();
        }
    }
}

