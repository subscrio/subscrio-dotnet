namespace Subscrio.Core.Application.DTOs;

public record CreateFeatureDto(
    string Key,
    string DisplayName,
    string ValueType,
    string DefaultValue,
    string? Description = null,
    string? GroupName = null,
    Dictionary<string, object?>? Validator = null,
    Dictionary<string, object?>? Metadata = null
);

public record UpdateFeatureDto(
    string? DisplayName = null,
    string? Description = null,
    string? ValueType = null,
    string? DefaultValue = null,
    string? GroupName = null,
    Dictionary<string, object?>? Validator = null,
    Dictionary<string, object?>? Metadata = null
);

public record FeatureDto(
    string Key,
    string DisplayName,
    string? Description,
    string ValueType,
    string DefaultValue,
    string? GroupName,
    string Status,
    Dictionary<string, object?>? Validator,
    Dictionary<string, object?>? Metadata,
    string CreatedAt,
    string UpdatedAt
);

public record FeatureFilterDto(
    string? Status = null,
    string? ValueType = null,
    string? GroupName = null,
    string? Search = null,
    string? SortBy = null,
    string? SortOrder = null,
    int Limit = 50,
    int Offset = 0
);

