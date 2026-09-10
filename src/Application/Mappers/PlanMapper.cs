using Subscrio.Core.Domain.Entities;
using Subscrio.Core.Domain.ValueObjects;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Infrastructure.Database;

namespace Subscrio.Core.Application.Mappers;

public static class PlanMapper
{
    public static PlanDto ToDto(Plan plan, string productKey, string? onExpireTransitionToBillingCycleKey = null)
    {
        return new PlanDto(
            ProductKey: productKey,
            Key: plan.Key,
            DisplayName: plan.DisplayName,
            Description: plan.Props.Description,
            Status: plan.Status.ToString().ToLowerInvariant(),
            OnExpireTransitionToBillingCycleKey: onExpireTransitionToBillingCycleKey,
            Metadata: plan.Props.Metadata,
            CreatedAt: plan.Props.CreatedAt.ToUniversalTime().ToString("O"),
            UpdatedAt: plan.Props.UpdatedAt.ToUniversalTime().ToString("O")
        );
    }

    public static Plan ToDomain(PlanRecord record, string productKey, string? onExpireTransitionToBillingCycleKey, List<PlanFeatureValue> featureValues)
    {
        return new Plan(
            new PlanProps
            {
                ProductKey = productKey,
                Key = record.Key,
                DisplayName = record.DisplayName,
                Description = record.Description,
                Status = Enum.Parse<PlanStatus>(record.Status, ignoreCase: true),
                OnExpireTransitionToBillingCycleKey = onExpireTransitionToBillingCycleKey,
                FeatureValues = featureValues,
                Metadata = record.Metadata,
                CreatedAt = record.CreatedAt,
                UpdatedAt = record.UpdatedAt
            },
            record.Id
        );
    }
}


