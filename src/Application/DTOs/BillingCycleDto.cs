namespace Subscrio.Core.Application.DTOs;

public record CreateBillingCycleDto(
    string PlanKey,
    string Key,
    string DisplayName,
    string DurationUnit,
    string? Description = null,
    int? DurationValue = null,
    string? ExternalProductId = null
);

public record UpdateBillingCycleDto(
    string? DisplayName = null,
    string? Description = null,
    int? DurationValue = null,
    string? DurationUnit = null,
    string? ExternalProductId = null
);

public record BillingCycleDto(
    string? ProductKey,
    string? PlanKey,
    string Key,
    string DisplayName,
    string? Description,
    string Status,
    int? DurationValue,
    string DurationUnit,
    string? ExternalProductId,
    string CreatedAt,
    string UpdatedAt
);

public record BillingCycleFilterDto(
    string? PlanKey = null,
    string? Status = null,
    int Limit = 50,
    int Offset = 0,
    string? DurationUnit = null,
    string? Search = null,
    string? SortBy = null,
    string? SortOrder = null
);

