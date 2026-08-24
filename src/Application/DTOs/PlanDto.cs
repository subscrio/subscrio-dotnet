namespace Subscrio.Core.Application.DTOs;

public record CreatePlanDto(
    string ProductKey,
    string Key,
    string DisplayName,
    string? Description = null,
    string? OnExpireTransitionToBillingCycleKey = null,
    Dictionary<string, object?>? Metadata = null
);

public record UpdatePlanDto(
    string? DisplayName = null,
    string? Description = null,
    string? OnExpireTransitionToBillingCycleKey = null,
    Dictionary<string, object?>? Metadata = null
);

public record PlanDto(
    string ProductKey,
    string Key,
    string DisplayName,
    string? Description,
    string Status,
    string? OnExpireTransitionToBillingCycleKey,
    Dictionary<string, object?>? Metadata,
    string CreatedAt,
    string UpdatedAt
);

public record PlanFilterDto(
    string? ProductKey = null,
    string? Status = null,
    string? Search = null,
    string? SortBy = null,
    string? SortOrder = null,
    int Limit = 50,
    int Offset = 0
);

public record PlanFeatureDto(
    string FeatureKey,
    string Value
);


