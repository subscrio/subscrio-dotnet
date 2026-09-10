using Subscrio.Core.Domain.Errors;
using Subscrio.Core.Domain.Base;
using Subscrio.Core.Domain.ValueObjects;

namespace Subscrio.Core.Domain.Entities;

public class FeatureOverride
{
    public required long FeatureId { get; init; }
    public required string Value { get; init; }
    public required OverrideType Type { get; init; }
    public required DateTime CreatedAt { get; init; }
}

public class SubscriptionProps
{
    public required string Key { get; init; } // External reference key for this subscription
    public required long CustomerId { get; set; }
    public required long PlanId { get; set; }
    public required long BillingCycleId { get; set; }
    public required SubscriptionStatus Status { get; set; }
    public required bool IsArchived { get; set; } // Archive flag - blocks updates but doesn't affect status calculation
    public DateTime? ActivationDate { get; set; }
    public DateTime? ExpirationDate { get; set; }
    public DateTime? CancellationDate { get; set; }
    public DateTime? TrialEndDate { get; set; }
    public DateTime? CurrentPeriodStart { get; set; }
    public DateTime? CurrentPeriodEnd { get; set; }
    public string? StripeSubscriptionId { get; set; }
    public List<FeatureOverride> FeatureOverrides { get; init; } = new();
    public Dictionary<string, object?>? Metadata { get; set; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; set; }
    public DateTime? TransitionedAt { get; set; } // UTC datetime when subscription was transitioned to a new plan
}

public class Subscription : Entity<SubscriptionProps>
{
    public Subscription(SubscriptionProps props, long? id = null) : base(props, id)
    {
    }

    public string Key => Props.Key;

    public long CustomerId => Props.CustomerId;

    public long PlanId => Props.PlanId;

    public SubscriptionStatus Status => Props.Status;

    public bool IsArchived => Props.IsArchived;

    public void Activate()
    {
        Props.ActivationDate ??= DateTime.UtcNow;
        Props.TrialEndDate = null; // Clear trial end date when activating
        Props.UpdatedAt = DateTime.UtcNow;
    }

    public void Cancel()
    {
        if (Status == SubscriptionStatus.Cancelled)
        {
            throw new DomainException($"Subscription is already cancelled. Current status: {Status}");
        }
        Props.CancellationDate = DateTime.UtcNow;
        Props.UpdatedAt = DateTime.UtcNow;
    }

    public void Renew()
    {
        // Clear temporary overrides on renewal
        ClearTemporaryOverrides();
        Props.UpdatedAt = DateTime.UtcNow;
    }

    public void Expire()
    {
        Props.ExpirationDate = DateTime.UtcNow;
        Props.UpdatedAt = DateTime.UtcNow;
    }

    public void Archive()
    {
        // Archive does not change any properties - just sets the archive flag
        Props.IsArchived = true;
        Props.UpdatedAt = DateTime.UtcNow;
    }

    public void MarkAsTransitioned()
    {
        // Mark subscription as transitioned - archives it and sets transitioned_at timestamp
        Props.IsArchived = true;
        Props.TransitionedAt = DateTime.UtcNow;
        Props.UpdatedAt = DateTime.UtcNow;
    }

    public void Unarchive()
    {
        // Unarchive just clears the archive flag
        Props.IsArchived = false;
        Props.UpdatedAt = DateTime.UtcNow;
    }

    public void SetExpirationDate(DateTime date)
    {
        Props.ExpirationDate = date;
        Props.UpdatedAt = DateTime.UtcNow;
    }

    public void SetActivationDate(DateTime date)
    {
        Props.ActivationDate = date;
        Props.UpdatedAt = DateTime.UtcNow;
    }

    public void SetTrialEndDate(DateTime? date)
    {
        Props.TrialEndDate = date;
        Props.UpdatedAt = DateTime.UtcNow;
    }

    public void SetCurrentPeriod(DateTime start, DateTime end)
    {
        Props.CurrentPeriodStart = start;
        Props.CurrentPeriodEnd = end;
        Props.UpdatedAt = DateTime.UtcNow;
    }

    public void AddFeatureOverride(long featureId, string value, OverrideType type)
    {
        // Remove existing override if present
        RemoveFeatureOverride(featureId);

        Props.FeatureOverrides.Add(new FeatureOverride
        {
            FeatureId = featureId,
            Value = value,
            Type = type,
            CreatedAt = DateTime.UtcNow
        });
        Props.UpdatedAt = DateTime.UtcNow;
    }

    public void RemoveFeatureOverride(long featureId)
    {
        Props.FeatureOverrides.RemoveAll(o => o.FeatureId == featureId);
        Props.UpdatedAt = DateTime.UtcNow;
    }

    public FeatureOverride? GetFeatureOverride(long featureId)
    {
        return Props.FeatureOverrides.Find(o => o.FeatureId == featureId);
    }

    public void ClearTemporaryOverrides()
    {
        Props.FeatureOverrides.RemoveAll(o => o.Type != OverrideType.Permanent);
        Props.UpdatedAt = DateTime.UtcNow;
    }

    // No deletion constraint - subscriptions can be deleted regardless of status
    public bool CanDelete()
    {
        return true;
    }
}
