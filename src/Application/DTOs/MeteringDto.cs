namespace Subscrio.Core.Application.DTOs;

public record MeteredFeatureConfigDto(string ResetPeriod, string Enforcement, string Aggregation, string UsageScope);
public record UsageOptions(string? SubscriptionKey = null, long RequestedUsage = 1);
public record UsageReportOptions(string IdempotencyKey, string? SubscriptionKey = null, Dictionary<string, object?>? Metadata = null);
public record UsageDto(bool HasAccess, long Limit, long Consumed, long Remaining, long RequestedUsage, long ProjectedConsumed, bool IsOverage, string Enforcement, string UsageScope, string? SubscriptionKey, string PeriodStart, string PeriodEnd, string? AccessDeniedReason = null);
public record UsageReportDto(string EventId, string IdempotencyKey, long Quantity, string RecordedAt, UsageDto Usage);
