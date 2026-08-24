namespace Subscrio.Core.Application.DTOs;

public record CreateCustomerDto(
    string Key,
    string? DisplayName = null,
    string? Email = null,
    string? ExternalBillingId = null,
    Dictionary<string, object?>? Metadata = null
);

public record UpdateCustomerDto(
    string? DisplayName = null,
    string? Email = null,
    string? ExternalBillingId = null,
    Dictionary<string, object?>? Metadata = null
);

/// <summary>
/// Mutable so before-hooks can edit New in place.
/// </summary>
public class CustomerDto
{
    public string Key { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string? ExternalBillingId { get; set; }
    public string Status { get; set; } = string.Empty;
    public Dictionary<string, object?>? Metadata { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
}

public record CustomerFilterDto(
    string? Status = null,
    string? Search = null,
    string? SortBy = null,
    string? SortOrder = null,
    int Limit = 50,
    int Offset = 0
);
