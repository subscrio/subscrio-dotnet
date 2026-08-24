using System.Text.Json;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Application.Errors;
using Subscrio.Core.Application.Services;

namespace Subscrio.Core.Application.Services;

/// <summary>
/// Configuration Sync Service
/// Syncs configuration from JSON files or programmatic DTOs to the database
/// Uses public Subscrio API methods to ensure all business logic is reused
/// </summary>
public class ConfigSyncService
{
    public ConfigSyncService(
        ProductManagementService products,
        FeatureManagementService features,
        PlanManagementService plans,
        BillingCycleManagementService billingCycles)
    {
        Products = products;
        Features = features;
        Plans = plans;
        BillingCycles = billingCycles;
    }

    private ProductManagementService Products { get; }
    private FeatureManagementService Features { get; }
    private PlanManagementService Plans { get; }
    private BillingCycleManagementService BillingCycles { get; }

    /// <summary>
    /// Load configuration from a JSON file and sync
    /// </summary>
    public async Task<ConfigSyncReport> SyncFromFileAsync(string filePath)
    {
        try
        {
            var fileContent = await File.ReadAllTextAsync(filePath);
            
            // Validate JSON property order before parsing
            ValidateConfigJsonPropertyOrder(fileContent);
            
            var jsonData = JsonSerializer.Deserialize<ConfigSyncDto>(fileContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            
            if (jsonData == null)
            {
                throw new ValidationException("Failed to parse configuration file: JSON is null");
            }
            
            return await SyncFromJsonAsync(jsonData);
        }
        catch (Exception error)
        {
            throw new ValidationException($"Failed to load configuration from file: {error.Message}");
        }
    }

    /// <summary>
    /// Sync configuration from a ConfigSyncDto object
    /// </summary>
    public async Task<ConfigSyncReport> SyncFromJsonAsync(ConfigSyncDto config)
    {
        // Initialize sync report
        var report = new ConfigSyncReport(
            Created: new ConfigSyncCounts(0, 0, 0, 0),
            Updated: new ConfigSyncCounts(0, 0, 0, 0),
            Archived: new ConfigSyncCounts(0, 0, 0, 0),
            Unarchived: new ConfigSyncCounts(0, 0, 0, 0),
            Ignored: new ConfigSyncCounts(0, 0, 0, 0),
            Errors: new List<ConfigSyncError>(),
            Warnings: new List<ConfigSyncWarning>()
        );

        // Phase 2: Load Current State
        var existingProducts = await Products.ListProductsAsync(new ProductFilterDto { Limit = 100, Offset = 0, SortOrder = "asc" });
        var existingFeatures = await Features.ListFeaturesAsync(new FeatureFilterDto { Limit = 100, Offset = 0 });
        var existingPlans = await Plans.ListPlansAsync(new PlanFilterDto { Limit = 100, Offset = 0, SortOrder = "asc" });
        var existingBillingCycles = await BillingCycles.ListBillingCyclesAsync(new BillingCycleFilterDto { Limit = 100, Offset = 0, SortOrder = "asc" });

        // Create lookup maps by key
        var productsByKey = existingProducts.ToDictionary(p => p.Key, p => p);
        var featuresByKey = existingFeatures.ToDictionary(f => f.Key, f => f);
        var plansByKey = existingPlans.ToDictionary(p => p.Key, p => p);
        var billingCyclesByKey = existingBillingCycles.ToDictionary(bc => bc.Key, bc => bc);

        // Track ignored entities (in database but not in config)
        var configFeatureKeys = new HashSet<string>(config.Features.Select(f => f.Key));
        var configProductKeys = new HashSet<string>(config.Products.Select(p => p.Key));
        var configPlanKeys = new HashSet<string>(
            config.Products.SelectMany(p => p.Plans ?? new List<PlanConfig>()).Select(plan => plan.Key)
        );
        var configBillingCycleKeys = new HashSet<string>(
            config.Products.SelectMany(p => 
                (p.Plans ?? new List<PlanConfig>()).SelectMany(plan => 
                    (plan.BillingCycles ?? new List<BillingCycleConfig>()).Select(bc => bc.Key)
                )
            )
        );

        report = report with
        {
            Ignored = new ConfigSyncCounts(
                existingFeatures.Count(f => !configFeatureKeys.Contains(f.Key)),
                existingProducts.Count(p => !configProductKeys.Contains(p.Key)),
                existingPlans.Count(p => !configPlanKeys.Contains(p.Key)),
                existingBillingCycles.Count(bc => !configBillingCycleKeys.Contains(bc.Key))
            )
        };

        // Phase 3: Sync Features (Independent Entities)
        foreach (var featureConfig in config.Features)
        {
            try
            {
                featuresByKey.TryGetValue(featureConfig.Key, out var existing);
                
                if (existing == null)
                {
                    // Create new feature
                    var createDto = new CreateFeatureDto(
                        featureConfig.Key,
                        featureConfig.DisplayName,
                        featureConfig.ValueType,
                        featureConfig.DefaultValue,
                        featureConfig.Description,
                        featureConfig.GroupName,
                        featureConfig.Validator,
                        featureConfig.Metadata
                    );
                    
                    await Features.CreateFeatureAsync(createDto);
                    report = report with
                    {
                        Created = report.Created with { Features = report.Created.Features + 1 }
                    };
                    
                    // Archive if needed
                    if (featureConfig.Archived == true)
                    {
                        await Features.ArchiveFeatureAsync(featureConfig.Key);
                        report = report with
                        {
                            Archived = report.Archived with { Features = report.Archived.Features + 1 }
                        };
                    }
                }
                else
                {
                    // Check if entity needs updating
                    var needsUpdate = HasFeatureChanges(featureConfig, existing);
                    
                    if (needsUpdate)
                    {
                        var updateDto = new UpdateFeatureDto(
                            DisplayName: featureConfig.DisplayName,
                            Description: featureConfig.Description,
                            ValueType: featureConfig.ValueType,
                            DefaultValue: featureConfig.DefaultValue,
                            GroupName: featureConfig.GroupName,
                            Validator: featureConfig.Validator,
                            Metadata: featureConfig.Metadata
                        );
                        
                        await Features.UpdateFeatureAsync(featureConfig.Key, updateDto);
                        report = report with
                        {
                            Updated = report.Updated with { Features = report.Updated.Features + 1 }
                        };
                    }
                    
                    // Handle archive status
                    var isArchived = existing.Status == "archived";
                    if (featureConfig.Archived == true && !isArchived)
                    {
                        await Features.ArchiveFeatureAsync(featureConfig.Key);
                        report = report with
                        {
                            Archived = report.Archived with { Features = report.Archived.Features + 1 }
                        };
                    }
                    else if (featureConfig.Archived == false && isArchived)
                    {
                        await Features.UnarchiveFeatureAsync(featureConfig.Key);
                        report = report with
                        {
                            Unarchived = report.Unarchived with { Features = report.Unarchived.Features + 1 }
                        };
                    }
                }
            }
            catch (Exception error)
            {
                var errors = new List<ConfigSyncError>(report.Errors)
                {
                    new ConfigSyncError("feature", featureConfig.Key, error.Message)
                };
                report = report with { Errors = errors };
            }
        }

        // Phase 4: Sync Products
        foreach (var productConfig in config.Products)
        {
            try
            {
                productsByKey.TryGetValue(productConfig.Key, out var existing);
                
                if (existing == null)
                {
                    // Create new product
                    var createDto = new CreateProductDto(
                        productConfig.Key,
                        productConfig.DisplayName,
                        productConfig.Description,
                        productConfig.Metadata
                    );
                    
                    await Products.CreateProductAsync(createDto);
                    report = report with
                    {
                        Created = report.Created with { Products = report.Created.Products + 1 }
                    };
                    
                    // Archive if needed
                    if (productConfig.Archived == true)
                    {
                        await Products.ArchiveProductAsync(productConfig.Key);
                        report = report with
                        {
                            Archived = report.Archived with { Products = report.Archived.Products + 1 }
                        };
                    }
                }
                else
                {
                    // Check if entity needs updating
                    var needsUpdate = HasProductChanges(productConfig, existing);
                    
                    if (needsUpdate)
                    {
                        var updateDto = new UpdateProductDto(
                            DisplayName: productConfig.DisplayName,
                            Description: productConfig.Description,
                            Metadata: productConfig.Metadata
                        );
                        
                        await Products.UpdateProductAsync(productConfig.Key, updateDto);
                        report = report with
                        {
                            Updated = report.Updated with { Products = report.Updated.Products + 1 }
                        };
                    }
                    
                    // Handle archive status
                    var isArchived = existing.Status == "archived";
                    if (productConfig.Archived == true && !isArchived)
                    {
                        await Products.ArchiveProductAsync(productConfig.Key);
                        report = report with
                        {
                            Archived = report.Archived with { Products = report.Archived.Products + 1 }
                        };
                    }
                    else if (productConfig.Archived == false && isArchived)
                    {
                        await Products.UnarchiveProductAsync(productConfig.Key);
                        report = report with
                        {
                            Unarchived = report.Unarchived with { Products = report.Unarchived.Products + 1 }
                        };
                    }
                }

                // Sync product-feature associations
                if (productConfig.Features != null)
                {
                    try
                    {
                        var currentFeatures = await Features.GetFeaturesByProductAsync(productConfig.Key);
                        var currentFeatureKeys = new HashSet<string>(currentFeatures.Select(f => f.Key));
                        var productFeatureKeys = new HashSet<string>(productConfig.Features);
                        
                        // Associate features in config but not in database
                        foreach (var featureKey in productConfig.Features)
                        {
                            if (!currentFeatureKeys.Contains(featureKey))
                            {
                                await Products.AssociateFeatureAsync(productConfig.Key, featureKey);
                            }
                        }

                        // Dissociate features in database but not in config
                        foreach (var feature in currentFeatures)
                        {
                            if (!productFeatureKeys.Contains(feature.Key))
                            {
                                await Products.DissociateFeatureAsync(productConfig.Key, feature.Key);
                            }
                        }
                    }
                    catch (Exception error)
                    {
                        var errors = new List<ConfigSyncError>(report.Errors)
                        {
                            new ConfigSyncError("product", productConfig.Key, $"Failed to sync feature associations: {error.Message}")
                        };
                        report = report with { Errors = errors };
                    }
                }
            }
            catch (Exception error)
            {
                var errors = new List<ConfigSyncError>(report.Errors)
                {
                    new ConfigSyncError("product", productConfig.Key, error.Message)
                };
                report = report with { Errors = errors };
            }
        }

        // Phase 5: Sync Plans (Dependent on Products)
        // Track plans that need onExpireTransitionToBillingCycleKey set after billing cycles are created
        var plansPendingTransitionKey = new List<(string PlanKey, string TransitionKey)>();
        
        foreach (var productConfig in config.Products)
        {
            if (productConfig.Plans == null) continue;

            foreach (var planConfig in productConfig.Plans)
            {
                try
                {
                    // Plan keys are globally unique, so lookup by key only
                    plansByKey.TryGetValue(planConfig.Key, out var existing);
                    
                    // Check if billing cycle exists in database or will be created in this config
                    var transitionBillingCycleKey = planConfig.OnExpireTransitionToBillingCycleKey;
                    var transitionBillingCycleExistsInDb = transitionBillingCycleKey != null &&
                        billingCyclesByKey.ContainsKey(transitionBillingCycleKey);
                    
                    // Check if billing cycle exists in config (will be created in this sync)
                    var transitionBillingCycleExistsInConfig = transitionBillingCycleKey != null &&
                        config.Products.Any(p => 
                            (p.Plans ?? new List<PlanConfig>()).Any(plan => 
                                (plan.BillingCycles ?? new List<BillingCycleConfig>()).Any(bc => bc.Key == transitionBillingCycleKey)
                            )
                        );
                    
                    if (existing == null)
                    {
                        // Create new plan
                        var createDto = new CreatePlanDto(
                            productConfig.Key,
                            planConfig.Key,
                            planConfig.DisplayName,
                            planConfig.Description,
                            transitionBillingCycleExistsInDb ? transitionBillingCycleKey : null,
                            planConfig.Metadata
                        );
                        
                        await Plans.CreatePlanAsync(createDto);
                        report = report with
                        {
                            Created = report.Created with { Plans = report.Created.Plans + 1 }
                        };
                        
                        // If billing cycle doesn't exist in DB yet but exists in config, defer setting the transition key
                        if (transitionBillingCycleKey != null && !transitionBillingCycleExistsInDb && transitionBillingCycleExistsInConfig)
                        {
                            plansPendingTransitionKey.Add((planConfig.Key, transitionBillingCycleKey));
                        }
                        
                        // Archive if needed
                        if (planConfig.Archived == true)
                        {
                            await Plans.ArchivePlanAsync(planConfig.Key);
                            report = report with
                            {
                                Archived = report.Archived with { Plans = report.Archived.Plans + 1 }
                            };
                        }
                    }
                    else
                    {
                        // Check if entity needs updating
                        var needsUpdate = HasPlanChanges(planConfig, existing);
                        
                        if (needsUpdate)
                        {
                            var updateDto = new UpdatePlanDto(
                                DisplayName: planConfig.DisplayName,
                                Description: planConfig.Description,
                                OnExpireTransitionToBillingCycleKey: transitionBillingCycleExistsInDb ? transitionBillingCycleKey : null,
                                Metadata: planConfig.Metadata
                            );
                            
                            // Only update if there are fields to update
                            if (updateDto.DisplayName != null || updateDto.Description != null || 
                                updateDto.OnExpireTransitionToBillingCycleKey != null || updateDto.Metadata != null)
                            {
                                await Plans.UpdatePlanAsync(planConfig.Key, updateDto);
                                report = report with
                                {
                                    Updated = report.Updated with { Plans = report.Updated.Plans + 1 }
                                };
                            }
                            
                            // If billing cycle doesn't exist in DB yet but exists in config, defer setting the transition key
                            if (transitionBillingCycleKey != null && !transitionBillingCycleExistsInDb && transitionBillingCycleExistsInConfig)
                            {
                                var currentTransitionKey = existing.OnExpireTransitionToBillingCycleKey;
                                if (currentTransitionKey != transitionBillingCycleKey)
                                {
                                    plansPendingTransitionKey.Add((planConfig.Key, transitionBillingCycleKey));
                                }
                            }
                        }
                        else if (transitionBillingCycleKey != null && !transitionBillingCycleExistsInDb && transitionBillingCycleExistsInConfig)
                        {
                            // Even if no other changes, we may need to defer setting the transition key
                            var currentTransitionKey = existing.OnExpireTransitionToBillingCycleKey;
                            if (currentTransitionKey != transitionBillingCycleKey)
                            {
                                plansPendingTransitionKey.Add((planConfig.Key, transitionBillingCycleKey));
                            }
                        }
                        
                        // Handle archive status
                        var isArchived = existing.Status == "archived";
                        if (planConfig.Archived == true && !isArchived)
                        {
                            await Plans.ArchivePlanAsync(planConfig.Key);
                            report = report with
                            {
                                Archived = report.Archived with { Plans = report.Archived.Plans + 1 }
                            };
                        }
                        else if (planConfig.Archived == false && isArchived)
                        {
                            await Plans.UnarchivePlanAsync(planConfig.Key);
                            report = report with
                            {
                                Unarchived = report.Unarchived with { Plans = report.Unarchived.Plans + 1 }
                            };
                        }
                    }

                    // Sync plan feature values
                    if (planConfig.FeatureValues != null)
                    {
                        try
                        {
                            var currentPlanFeatures = await Plans.GetPlanFeaturesAsync(planConfig.Key);
                            var currentFeatureMap = currentPlanFeatures.ToDictionary(f => f.FeatureKey, f => f.Value);
                            var planFeatureKeys = new HashSet<string>(planConfig.FeatureValues.Keys);

                            // Set feature values in config (only if changed)
                            foreach (var (featureKey, value) in planConfig.FeatureValues)
                            {
                                currentFeatureMap.TryGetValue(featureKey, out var currentValue);
                                
                                // Only update if value changed
                                if (currentValue != value)
                                {
                                    var feature = await Features.GetFeatureAsync(featureKey);
                                    if (feature == null)
                                    {
                                        var warnings = new List<ConfigSyncWarning>(report.Warnings)
                                        {
                                            new ConfigSyncWarning("plan", planConfig.Key, $"Feature '{featureKey}' not found, skipping feature value")
                                        };
                                        report = report with { Warnings = warnings };
                                        continue;
                                    }
                                    
                                    await Plans.SetFeatureValueAsync(planConfig.Key, featureKey, value);
                                }
                            }

                            // Remove feature values not in config
                            foreach (var planFeature in currentPlanFeatures)
                            {
                                if (!planFeatureKeys.Contains(planFeature.FeatureKey))
                                {
                                    await Plans.RemoveFeatureValueAsync(planConfig.Key, planFeature.FeatureKey);
                                }
                            }
                        }
                        catch (Exception error)
                        {
                            var errors = new List<ConfigSyncError>(report.Errors)
                            {
                                new ConfigSyncError("plan", planConfig.Key, $"Failed to sync feature values: {error.Message}")
                            };
                            report = report with { Errors = errors };
                        }
                    }
                }
                catch (Exception error)
                {
                    var errors = new List<ConfigSyncError>(report.Errors)
                    {
                        new ConfigSyncError("plan", planConfig.Key, error.Message)
                    };
                    report = report with { Errors = errors };
                }
            }
        }

        // Phase 6: Sync Billing Cycles (Dependent on Plans)
        foreach (var productConfig in config.Products)
        {
            if (productConfig.Plans == null) continue;

            foreach (var planConfig in productConfig.Plans)
            {
                if (planConfig.BillingCycles == null) continue;

                foreach (var billingCycleConfig in planConfig.BillingCycles)
                {
                    try
                    {
                        // Billing cycle keys are globally unique, so lookup by key only
                        billingCyclesByKey.TryGetValue(billingCycleConfig.Key, out var existing);
                        
                        if (existing == null)
                        {
                            // Create new billing cycle
                            var createDto = new CreateBillingCycleDto(
                                planConfig.Key,
                                billingCycleConfig.Key,
                                billingCycleConfig.DisplayName,
                                billingCycleConfig.DurationUnit,
                                billingCycleConfig.Description,
                                billingCycleConfig.DurationValue,
                                billingCycleConfig.ExternalProductId
                            );
                            
                            await BillingCycles.CreateBillingCycleAsync(createDto);
                            report = report with
                            {
                                Created = report.Created with { BillingCycles = report.Created.BillingCycles + 1 }
                            };
                            
                            // Archive if needed
                            if (billingCycleConfig.Archived == true)
                            {
                                await BillingCycles.ArchiveBillingCycleAsync(billingCycleConfig.Key);
                                report = report with
                                {
                                    Archived = report.Archived with { BillingCycles = report.Archived.BillingCycles + 1 }
                                };
                            }
                        }
                        else
                        {
                            // Check if entity needs updating
                            var needsUpdate = HasBillingCycleChanges(billingCycleConfig, existing);
                            
                            if (needsUpdate)
                            {
                                var updateDto = new UpdateBillingCycleDto(
                                    DisplayName: billingCycleConfig.DisplayName,
                                    Description: billingCycleConfig.Description,
                                    DurationValue: billingCycleConfig.DurationValue,
                                    DurationUnit: billingCycleConfig.DurationUnit,
                                    ExternalProductId: billingCycleConfig.ExternalProductId
                                );
                                
                                await BillingCycles.UpdateBillingCycleAsync(billingCycleConfig.Key, updateDto);
                                report = report with
                                {
                                    Updated = report.Updated with { BillingCycles = report.Updated.BillingCycles + 1 }
                                };
                            }
                            
                            // Handle archive status
                            var isArchived = existing.Status == "archived";
                            if (billingCycleConfig.Archived == true && !isArchived)
                            {
                                await BillingCycles.ArchiveBillingCycleAsync(billingCycleConfig.Key);
                                report = report with
                                {
                                    Archived = report.Archived with { BillingCycles = report.Archived.BillingCycles + 1 }
                                };
                            }
                            else if (billingCycleConfig.Archived == false && isArchived)
                            {
                                await BillingCycles.UnarchiveBillingCycleAsync(billingCycleConfig.Key);
                                report = report with
                                {
                                    Unarchived = report.Unarchived with { BillingCycles = report.Unarchived.BillingCycles + 1 }
                                };
                            }
                        }
                    }
                    catch (Exception error)
                    {
                        var errors = new List<ConfigSyncError>(report.Errors)
                        {
                            new ConfigSyncError("billingCycle", billingCycleConfig.Key, error.Message)
                        };
                        report = report with { Errors = errors };
                    }
                }
            }
        }

        // Phase 7: Set deferred onExpireTransitionToBillingCycleKey values
        // Now that all billing cycles are created, update plans that were waiting for their transition keys
        foreach (var (planKey, transitionKey) in plansPendingTransitionKey)
        {
            try
            {
                // Verify billing cycle now exists
                var billingCycle = await BillingCycles.GetBillingCycleAsync(transitionKey);
                if (billingCycle != null)
                {
                    await Plans.UpdatePlanAsync(planKey, new UpdatePlanDto
                    {
                        OnExpireTransitionToBillingCycleKey = transitionKey
                    });
                }
                else
                {
                    var errors = new List<ConfigSyncError>(report.Errors)
                    {
                        new ConfigSyncError("plan", planKey, $"Billing cycle key '{transitionKey}' referenced in onExpireTransitionToBillingCycleKey does not exist")
                    };
                    report = report with { Errors = errors };
                }
            }
            catch (Exception error)
            {
                var errors = new List<ConfigSyncError>(report.Errors)
                {
                    new ConfigSyncError("plan", planKey, $"Failed to set onExpireTransitionToBillingCycleKey: {error.Message}")
                };
                report = report with { Errors = errors };
            }
        }

        return report;
    }

    // Helper methods
    private static bool DeepEqual(object? a, object? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a == null || b == null) return a == b;
        if (a.GetType() != b.GetType()) return false;
        
        // For dictionaries, compare key-value pairs
        if (a is Dictionary<string, object?> dictA && b is Dictionary<string, object?> dictB)
        {
            if (dictA.Count != dictB.Count) return false;
            foreach (var (key, valueA) in dictA)
            {
                if (!dictB.TryGetValue(key, out var valueB)) return false;
                if (!DeepEqual(valueA, valueB)) return false;
            }
            return true;
        }
        
        // For simple types, use equality
        return a.Equals(b);
    }

    private static string? NormalizeValue(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return value;
    }

    private static bool HasFeatureChanges(FeatureConfig config, FeatureDto existing)
    {
        if (config.DisplayName != existing.DisplayName) return true;
        if (NormalizeValue(config.Description) != NormalizeValue(existing.Description)) return true;
        if (config.ValueType != existing.ValueType) return true;
        if (config.DefaultValue != existing.DefaultValue) return true;
        if (NormalizeValue(config.GroupName) != NormalizeValue(existing.GroupName)) return true;
        if (!DeepEqual(config.Validator ?? null, existing.Validator ?? null)) return true;
        if (!DeepEqual(config.Metadata ?? null, existing.Metadata ?? null)) return true;
        return false;
    }

    private static bool HasProductChanges(ProductConfig config, ProductDto existing)
    {
        if (config.DisplayName != existing.DisplayName) return true;
        if (NormalizeValue(config.Description) != NormalizeValue(existing.Description)) return true;
        if (!DeepEqual(config.Metadata ?? null, existing.Metadata ?? null)) return true;
        return false;
    }

    private static bool HasPlanChanges(PlanConfig config, PlanDto existing)
    {
        if (config.DisplayName != existing.DisplayName) return true;
        if (NormalizeValue(config.Description) != NormalizeValue(existing.Description)) return true;
        if (NormalizeValue(config.OnExpireTransitionToBillingCycleKey) != NormalizeValue(existing.OnExpireTransitionToBillingCycleKey)) return true;
        if (!DeepEqual(config.Metadata ?? null, existing.Metadata ?? null)) return true;
        return false;
    }

    private static bool HasBillingCycleChanges(BillingCycleConfig config, BillingCycleDto existing)
    {
        if (config.DisplayName != existing.DisplayName) return true;
        if (NormalizeValue(config.Description) != NormalizeValue(existing.Description)) return true;
        if (config.DurationValue != existing.DurationValue) return true;
        if (config.DurationUnit != existing.DurationUnit) return true;
        if (NormalizeValue(config.ExternalProductId) != NormalizeValue(existing.ExternalProductId)) return true;
        return false;
    }

    /// <summary>
    /// Validate JSON property order (ensures consistent ordering for version control)
    /// </summary>
    private static void ValidateConfigJsonPropertyOrder(string jsonString)
    {
        // In C#, we can't easily validate property order without custom JSON parsing
        // For now, we'll skip this validation - it's mainly for developer experience
        // The JSON will still be parsed and validated by the DTO structure
    }
}
