namespace Subscrio.Core.Application.DTOs;

public record BillingCycleConfig(
    string Key,
    string DisplayName,
    string? Description = null,
    int? DurationValue = null,
    string DurationUnit = "days",
    string? ExternalProductId = null,
    bool? Archived = null
);

public record PlanConfig(
    string Key,
    string DisplayName,
    string? Description = null,
    string? OnExpireTransitionToBillingCycleKey = null,
    Dictionary<string, string>? FeatureValues = null,
    List<BillingCycleConfig>? BillingCycles = null,
    Dictionary<string, object?>? Metadata = null,
    bool? Archived = null
);

public record FeatureConfig(
    string Key,
    string DisplayName,
    string? Description = null,
    string ValueType = "toggle",
    string DefaultValue = "false",
    string? GroupName = null,
    Dictionary<string, object?>? Validator = null,
    Dictionary<string, object?>? Metadata = null,
    bool? Archived = null
);

public record ProductConfig(
    string Key,
    string DisplayName,
    string? Description = null,
    Dictionary<string, object?>? Metadata = null,
    bool? Archived = null,
    List<string>? Features = null,
    List<PlanConfig>? Plans = null
);

public record ConfigSyncDto(
    string Version,
    List<FeatureConfig> Features,
    List<ProductConfig> Products
);

public record ConfigSyncCounts(
    int Features,
    int Products,
    int Plans,
    int BillingCycles
);

public record ConfigSyncReport(
    ConfigSyncCounts Created,
    ConfigSyncCounts Updated,
    ConfigSyncCounts Archived,
    ConfigSyncCounts Unarchived,
    ConfigSyncCounts Ignored,
    List<ConfigSyncError> Errors,
    List<ConfigSyncWarning> Warnings
);

public record ConfigSyncError(
    string EntityType,
    string Key,
    string Message
);

public record ConfigSyncWarning(
    string EntityType,
    string Key,
    string Message
);


