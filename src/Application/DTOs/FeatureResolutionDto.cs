namespace Subscrio.Core.Application.DTOs;

public record FeatureResolutionOptions(string? AddonRule = null, string? SubscriptionRule = null);
public record FeatureValueSourceDto(string Kind, string Key, string Value, bool Applied, int? Quantity = null, string? ExpiresAt = null, string? Reason = null);
public record SubscriptionFeatureValueDto(string SubscriptionKey, string Value, List<FeatureValueSourceDto> Sources);
public record FeatureValueExplanationDto(string EvaluatedAt, string EffectiveValue, FeatureResolutionOptions Resolution, List<SubscriptionFeatureValueDto> Subscriptions);
