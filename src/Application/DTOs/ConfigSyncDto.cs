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
    bool? Archived = null,
    List<PlanCreditGrantDto>? CreditGrants = null
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
    bool? Archived = null,
    MeteredFeatureConfigDto? MeteredConfig = null,
    List<CreditConsumptionRuleDto>? CreditConsumptionRules = null
);

public record ProductConfig(
    string Key,
    string DisplayName,
    string? Description = null,
    Dictionary<string, object?>? Metadata = null,
    bool? Archived = null,
    List<string>? Features = null,
    List<PlanConfig>? Plans = null,
    List<AddonConfig>? Addons = null,
    Dictionary<string, FeatureResolutionOptions>? FeatureResolution = null
);

public record ConfigSyncDto(
    string Version,
    List<FeatureConfig> Features,
    List<ProductConfig> Products,
    List<CreditCurrencyConfig>? CreditCurrencies = null,
    List<SubscriptionOverrideConfig>? Subscriptions = null,
    List<CreditConsumptionConfig>? CreditConsumptionRules = null
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
)
{
    public AccountingSyncReport? Details
    {
        get; set;
    }
}

public record AccountingSyncChange(string EntityType, string Key, string Action);
public record AccountingSyncReport(int Created, int Updated, int Removed, int Unchanged, List<AccountingSyncChange> Changes);

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



public record CreditCurrencyConfig(string Key, string DisplayName, bool? Archived = null, Dictionary<string, object?>? Metadata = null);
public record AddonConfig(string Key, string DisplayName, string? Description = null, string? CompositionMode = null, int? Priority = null, bool? Archived = null, Dictionary<string, object?>? Metadata = null, Dictionary<string, string>? FeatureValues = null);
public record SubscriptionOverrideConfig(string Key, List<FeatureOverrideConfig> FeatureOverrides);
public record FeatureOverrideConfig(string FeatureKey, string? Value = null, string? Type = null, DateTime? ExpiresAt = null, bool Remove = false);

public record CreditConsumptionConfig(string FeatureKey, string CurrencyKey, long CreditsPerUnit);
