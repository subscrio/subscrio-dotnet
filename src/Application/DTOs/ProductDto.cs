namespace Subscrio.Core.Application.DTOs;

public record CreateProductDto(
    string Key,
    string DisplayName,
    string? Description = null,
    Dictionary<string, object?>? Metadata = null
);

public record UpdateProductDto(
    string? DisplayName = null,
    string? Description = null,
    Dictionary<string, object?>? Metadata = null
);

public record ProductDto(
    string Key,
    string DisplayName,
    string? Description,
    string Status,
    Dictionary<string, object?>? Metadata,
    string CreatedAt,
    string UpdatedAt
);

public record ProductFilterDto(
    string? Status = null,
    string? Search = null,
    int Limit = 50,
    int Offset = 0,
    string? SortBy = null,
    string SortOrder = "asc"
);


