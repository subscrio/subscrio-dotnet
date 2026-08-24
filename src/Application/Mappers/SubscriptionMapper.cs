using Subscrio.Core.Domain.Entities;
using Subscrio.Core.Domain.ValueObjects;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Infrastructure.Database;
using Subscrio.Core.Infrastructure.Utils;

namespace Subscrio.Core.Application.Mappers;

public static class SubscriptionMapper
{
    public static SubscriptionDto ToDto(
        Subscription subscription,
        string customerKey,
        string productKey,
        string planKey,
        string billingCycleKey,
        CustomerDto? customer = null)
    {
        return new SubscriptionDto
        {
            Key = subscription.Key,
            CustomerKey = customerKey,
            ProductKey = productKey,
            PlanKey = planKey,
            BillingCycleKey = billingCycleKey,
            Status = subscription.Status.ToString().ToLowerInvariant(),
            IsArchived = subscription.IsArchived,
            ActivationDate = subscription.Props.ActivationDate?.ToUniversalTime().ToString("O"),
            ExpirationDate = subscription.Props.ExpirationDate?.ToUniversalTime().ToString("O"),
            CancellationDate = subscription.Props.CancellationDate?.ToUniversalTime().ToString("O"),
            TrialEndDate = subscription.Props.TrialEndDate?.ToUniversalTime().ToString("O"),
            CurrentPeriodStart = subscription.Props.CurrentPeriodStart?.ToUniversalTime().ToString("O"),
            CurrentPeriodEnd = subscription.Props.CurrentPeriodEnd?.ToUniversalTime().ToString("O"),
            StripeSubscriptionId = subscription.Props.StripeSubscriptionId,
            Metadata = subscription.Props.Metadata,
            Customer = customer,
            CreatedAt = subscription.Props.CreatedAt.ToUniversalTime().ToString("O"),
            UpdatedAt = subscription.Props.UpdatedAt.ToUniversalTime().ToString("O")
        };
    }

    /// <summary>
    /// Maps a SubscriptionStatusViewRecord (from database view) to a Subscription domain entity.
    /// This is the preferred method as it uses the computed status from the database view.
    /// </summary>
    public static Subscription ToDomain(SubscriptionStatusViewRecord record, List<FeatureOverride> featureOverrides)
    {
        var status = Enum.Parse<SubscriptionStatus>(record.ComputedStatus, ignoreCase: true);
        
        return new Subscription(
            new SubscriptionProps
            {
                Key = record.Key,
                CustomerId = record.CustomerId,
                PlanId = record.PlanId,
                BillingCycleId = record.BillingCycleId,
                Status = status, // Use computed status from view
                IsArchived = record.IsArchived,
                ActivationDate = record.ActivationDate,
                ExpirationDate = record.ExpirationDate,
                CancellationDate = record.CancellationDate,
                TrialEndDate = record.TrialEndDate,
                CurrentPeriodStart = record.CurrentPeriodStart,
                CurrentPeriodEnd = record.CurrentPeriodEnd,
                StripeSubscriptionId = record.StripeSubscriptionId,
                FeatureOverrides = featureOverrides,
                Metadata = record.Metadata,
                CreatedAt = record.CreatedAt,
                UpdatedAt = record.UpdatedAt,
                TransitionedAt = record.TransitionedAt
            },
            record.Id
        );
    }

    /// <summary>
    /// Maps a SubscriptionRecord (from table) to a Subscription domain entity.
    /// This method computes status in application code and should only be used when the view is not available.
    /// </summary>
    public static Subscription ToDomain(SubscriptionRecord record, List<FeatureOverride> featureOverrides)
    {
        var computedStatus = ComputeStatus(record);
        
        return new Subscription(
            new SubscriptionProps
            {
                Key = record.Key,
                CustomerId = record.CustomerId,
                PlanId = record.PlanId,
                BillingCycleId = record.BillingCycleId,
                Status = computedStatus,
                IsArchived = record.IsArchived,
                ActivationDate = record.ActivationDate,
                ExpirationDate = record.ExpirationDate,
                CancellationDate = record.CancellationDate,
                TrialEndDate = record.TrialEndDate,
                CurrentPeriodStart = record.CurrentPeriodStart,
                CurrentPeriodEnd = record.CurrentPeriodEnd,
                StripeSubscriptionId = record.StripeSubscriptionId,
                FeatureOverrides = featureOverrides,
                Metadata = record.Metadata,
                CreatedAt = record.CreatedAt,
                UpdatedAt = record.UpdatedAt,
                TransitionedAt = record.TransitionedAt
            },
            record.Id
        );
    }

    public static SubscriptionRecord ToPersistence(Subscription subscription)
    {
        return new SubscriptionRecord
        {
            Id = subscription.Id ?? 0,
            Key = subscription.Key,
            CustomerId = subscription.CustomerId,
            PlanId = subscription.PlanId,
            BillingCycleId = subscription.Props.BillingCycleId,
            IsArchived = subscription.IsArchived,
            ActivationDate = subscription.Props.ActivationDate,
            ExpirationDate = subscription.Props.ExpirationDate,
            CancellationDate = subscription.Props.CancellationDate,
            TrialEndDate = subscription.Props.TrialEndDate,
            CurrentPeriodStart = subscription.Props.CurrentPeriodStart,
            CurrentPeriodEnd = subscription.Props.CurrentPeriodEnd,
            StripeSubscriptionId = subscription.Props.StripeSubscriptionId,
            Metadata = subscription.Props.Metadata,
            CreatedAt = subscription.Props.CreatedAt,
            UpdatedAt = subscription.Props.UpdatedAt,
            TransitionedAt = subscription.Props.TransitionedAt
        };
    }

    private static SubscriptionStatus ComputeStatus(SubscriptionRecord record)
    {
        var now = DateHelper.Now();

        // Check cancellation status first
        if (record.CancellationDate.HasValue)
        {
            if (record.CancellationDate.Value > now)
            {
                return SubscriptionStatus.CancellationPending;
            }
            else
            {
                return SubscriptionStatus.Cancelled;
            }
        }

        // Check expiration
        if (record.ExpirationDate.HasValue && record.ExpirationDate.Value <= now)
        {
            return SubscriptionStatus.Expired;
        }

        // Check activation (pending)
        if (record.ActivationDate.HasValue && record.ActivationDate.Value > now)
        {
            return SubscriptionStatus.Pending;
        }

        // Check trial
        if (record.TrialEndDate.HasValue && record.TrialEndDate.Value > now)
        {
            return SubscriptionStatus.Trial;
        }

        // Default to active
        return SubscriptionStatus.Active;
    }
}
