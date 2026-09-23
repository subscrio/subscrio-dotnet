namespace Subscrio.Core.Application.DTOs;

public record CreateSubscriptionDto(
    string Key,
    string CustomerKey,
    string BillingCycleKey,
    DateTime? ActivationDate = null,
    DateTime? ExpirationDate = null,
    DateTime? CancellationDate = null,
    DateTime? TrialEndDate = null,
    DateTime? CurrentPeriodStart = null,
    DateTime? CurrentPeriodEnd = null,
    string? StripeSubscriptionId = null,
    Dictionary<string, object?>? Metadata = null
);

public record UpdateSubscriptionDto(
    string? BillingCycleKey = null,
    DateTime? ExpirationDate = null,
    DateTime? CancellationDate = null,
    DateTime? TrialEndDate = null,
    bool ClearTrialEndDate = false,
    DateTime? CurrentPeriodStart = null,
    DateTime? CurrentPeriodEnd = null,
    string? StripeSubscriptionId = null,
    Dictionary<string, object?>? Metadata = null
);

/// <summary>
/// Mutable so before-hooks can edit New in place.
/// </summary>
public class SubscriptionDto
{
    public List<SubscriptionAddonDto> Addons { get; set; } = [];
    public List<FeatureOverrideDto> FeatureOverrides { get; set; } = new();
    public string Key { get; set; } = string.Empty;
    public string CustomerKey { get; set; } = string.Empty;
    public string ProductKey { get; set; } = string.Empty;
    public string PlanKey { get; set; } = string.Empty;
    public string BillingCycleKey { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsArchived
    {
        get; set;
    }
    public string? ActivationDate
    {
        get; set;
    }
    public string? ExpirationDate
    {
        get; set;
    }
    public string? CancellationDate
    {
        get; set;
    }
    public string? TrialEndDate
    {
        get; set;
    }
    public string? CurrentPeriodStart
    {
        get; set;
    }
    public string? CurrentPeriodEnd
    {
        get; set;
    }
    public string? StripeSubscriptionId
    {
        get; set;
    }
    public Dictionary<string, object?>? Metadata
    {
        get; set;
    }
    public CustomerDto? Customer
    {
        get; set;
    }
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;

    public SubscriptionDto Clone() => new()
    {
        Addons = new(Addons),
        FeatureOverrides = new(FeatureOverrides),
        Key = Key,
        CustomerKey = CustomerKey,
        ProductKey = ProductKey,
        PlanKey = PlanKey,
        BillingCycleKey = BillingCycleKey,
        Status = Status,
        IsArchived = IsArchived,
        ActivationDate = ActivationDate,
        ExpirationDate = ExpirationDate,
        CancellationDate = CancellationDate,
        TrialEndDate = TrialEndDate,
        CurrentPeriodStart = CurrentPeriodStart,
        CurrentPeriodEnd = CurrentPeriodEnd,
        StripeSubscriptionId = StripeSubscriptionId,
        Metadata = Metadata,
        Customer = Customer,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt
    };
}

public record SubscriptionFilterDto(
    string? CustomerKey = null,
    string? ProductKey = null,
    string? PlanKey = null,
    string? Status = null,
    bool? IsArchived = null,
    string? SortBy = null,
    string? SortOrder = null,
    int? Limit = null,
    int? Offset = null
);

public record DetailedSubscriptionFilterDto(
    string? CustomerKey = null,
    string? ProductKey = null,
    string? PlanKey = null,
    string? BillingCycleKey = null,
    string? Status = null,
    bool? IsArchived = null,
    DateTime? ActivationDateFrom = null,
    DateTime? ActivationDateTo = null,
    DateTime? ExpirationDateFrom = null,
    DateTime? ExpirationDateTo = null,
    DateTime? TrialEndDateFrom = null,
    DateTime? TrialEndDateTo = null,
    DateTime? CurrentPeriodStartFrom = null,
    DateTime? CurrentPeriodStartTo = null,
    DateTime? CurrentPeriodEndFrom = null,
    DateTime? CurrentPeriodEndTo = null,
    bool? HasStripeId = null,
    bool? HasTrial = null,
    bool? HasFeatureOverrides = null,
    string? FeatureKey = null,
    string? MetadataKey = null,
    object? MetadataValue = null,
    string? SortBy = null,
    string? SortOrder = null,
    int? Limit = null,
    int? Offset = null
);

public record FeatureOverrideDto(
    long FeatureId,
    string Value,
    string Type,
    string CreatedAt,
    string? FeatureKey = null,
    string? ExpiresAt = null,
    bool IsActive = true
);

public record TransitionExpiredSubscriptionsReport(
    int Processed,
    int Transitioned,
    int Archived,
    List<TransitionError> Errors
);

public record TransitionError(
    string SubscriptionKey,
    string Error
);
