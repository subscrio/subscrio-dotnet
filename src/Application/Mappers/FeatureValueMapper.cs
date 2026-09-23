using Subscrio.Core.Domain.Entities;
using Subscrio.Core.Domain.ValueObjects;
using Subscrio.Core.Infrastructure.Database;

namespace Subscrio.Core.Application.Mappers;

/// <summary>
/// Maps plan feature values and subscription overrides from EF records to domain types.
/// </summary>
public static class FeatureValueMapper
{
    public static List<PlanFeatureValue> ToPlanFeatureValues(
        IEnumerable<PlanFeatureRecord> records) =>
        records.Select(r => new PlanFeatureValue
        {
            FeatureId = r.FeatureId,
            Value = r.Value,
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt
        }).ToList();

    public static List<FeatureOverride> ToFeatureOverrides(
        IEnumerable<SubscriptionFeatureOverrideRecord> records) =>
        records.Select(r => new FeatureOverride
        {
            FeatureId = r.FeatureId,
            Value = r.Value,
            Type = Enum.Parse<OverrideType>(r.OverrideType, ignoreCase: true),
            ExpiresAt = r.ExpiresAt,
            CreatedAt = r.CreatedAt
        }).ToList();
}
