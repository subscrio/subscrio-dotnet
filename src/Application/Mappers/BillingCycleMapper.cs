using Subscrio.Core.Domain.Entities;
using Subscrio.Core.Domain.ValueObjects;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Infrastructure.Database;

namespace Subscrio.Core.Application.Mappers;

public static class BillingCycleMapper
{
    public static BillingCycleDto ToDto(BillingCycle billingCycle, string? productKey = null, string? planKey = null)
    {
        return new BillingCycleDto(
            ProductKey: productKey,
            PlanKey: planKey,
            Key: billingCycle.Key,
            DisplayName: billingCycle.DisplayName,
            Description: billingCycle.Props.Description,
            Status: billingCycle.Status.ToString().ToLowerInvariant(),
            DurationValue: billingCycle.Props.DurationValue,
            DurationUnit: billingCycle.Props.DurationUnit.ToString().ToLowerInvariant(),
            ExternalProductId: billingCycle.Props.ExternalProductId,
            CreatedAt: billingCycle.Props.CreatedAt.ToUniversalTime().ToString("O"),
            UpdatedAt: billingCycle.Props.UpdatedAt.ToUniversalTime().ToString("O")
        );
    }

    public static BillingCycle ToDomain(BillingCycleRecord record)
    {
        return new BillingCycle(
            new BillingCycleProps
            {
                PlanId = record.PlanId,
                Key = record.Key,
                DisplayName = record.DisplayName,
                Description = record.Description,
                Status = Enum.Parse<BillingCycleStatus>(record.Status, ignoreCase: true),
                DurationValue = record.DurationValue,
                DurationUnit = Enum.Parse<DurationUnit>(record.DurationUnit, ignoreCase: true),
                ExternalProductId = record.ExternalProductId,
                CreatedAt = record.CreatedAt,
                UpdatedAt = record.UpdatedAt
            },
            record.Id
        );
    }

    public static BillingCycleRecord ToPersistence(BillingCycle billingCycle)
    {
        return new BillingCycleRecord
        {
            Id = billingCycle.Id ?? 0,
            PlanId = billingCycle.PlanId,
            Key = billingCycle.Key,
            DisplayName = billingCycle.DisplayName,
            Description = billingCycle.Props.Description,
            Status = billingCycle.Status.ToString().ToLowerInvariant(),
            DurationValue = billingCycle.Props.DurationValue,
            DurationUnit = billingCycle.Props.DurationUnit.ToString().ToLowerInvariant(),
            ExternalProductId = billingCycle.Props.ExternalProductId,
            CreatedAt = billingCycle.Props.CreatedAt,
            UpdatedAt = billingCycle.Props.UpdatedAt
        };
    }
}
