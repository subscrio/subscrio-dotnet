namespace Subscrio.Core.Application.DTOs;

public record CreateCreditCurrencyDto(string Key, string DisplayName, Dictionary<string, object?>? Metadata = null);
public record CreditCurrencyDto(string Key, string DisplayName, string Status, Dictionary<string, object?>? Metadata, string CreatedAt, string UpdatedAt);
public record PlanCreditGrantInput(long Amount, string Cadence, string ExpiryPolicy = "none", string CancellationPolicy = "retain");
public record PlanCreditGrantDto(string CurrencyKey, long Amount, string Cadence, string ExpiryPolicy, string CancellationPolicy);
public record CreditConsumptionRuleDto(string CurrencyKey, long CreditsPerUnit);
public record CreditActionDto(string CustomerKey, string FeatureKey, long Units);
public record CreditCostDto(string CurrencyKey, long Cost, long Available);
public record CreditCheckDto(bool HasAccess, List<CreditCostDto> Costs, string? AccessDeniedReason = null);
public record CreditGrantInput(string CustomerKey, string CurrencyKey, long Amount, string GrantType, string IdempotencyKey, int Priority = 0, DateTime? ExpiresAt = null, string? SubscriptionKey = null, Dictionary<string, object?>? Metadata = null);
public record CreditConsumeInput(string CustomerKey, string FeatureKey, long Units, string IdempotencyKey, Dictionary<string, object?>? Metadata = null);
public record CreditAdjustInput(string CustomerKey, string CurrencyKey, long Amount, string Reason, string IdempotencyKey);
public record CreditAllocationDto(string CurrencyKey, string GrantId, long Amount);
public record CreditAvailableDto(string CurrencyKey, long Available);
public record CreditConsumeDto(string OperationId, string IdempotencyKey, List<CreditAllocationDto> Allocations, List<CreditAvailableDto> Balances);
public record CreditGrantDto(string Id, string CurrencyKey, string? SubscriptionKey, string GrantType, long OriginalAmount, long RemainingAmount, int Priority, string? ExpiresAt, string CreatedAt, string UpdatedAt);
public record CreditBalanceDto(string CurrencyKey, long Available, List<CreditGrantDto> Grants);
public record CreditLedgerEntryDto(string Id, string OperationId, string GrantId, long Amount, string Reason, string CreatedAt, Dictionary<string, object?>? Metadata);
public record CreditOperationDto(string Id, string Type, System.Text.Json.JsonElement Result, string CreatedAt);

public record CreditAdjustmentDto(string OperationId, string IdempotencyKey, CreditBalanceDto Balance);

public record IssueDuePlanGrantsInput(string SubscriptionKey);
public record DuePlanGrantsDto(List<CreditGrantDto> Issued, string? NextDueAt, bool HasMore);
