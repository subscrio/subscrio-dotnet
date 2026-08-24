using Subscrio.Core.Application.Errors;
using Subscrio.Core.Domain.Base;
using Subscrio.Core.Domain.ValueObjects;
using Subscrio.Core.Infrastructure.Utils;

namespace Subscrio.Core.Domain.Entities;

public class PlanFeatureValue
{
    public required long FeatureId { get; init; }
    public required string Value { get; set; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; set; }
}

public class PlanProps
{
    public required string ProductKey { get; init; }
    public required string Key { get; init; }
    public required string DisplayName { get; set; }
    public string? Description { get; set; }
    public required PlanStatus Status { get; set; }
    public string? OnExpireTransitionToBillingCycleKey { get; set; }
    public List<PlanFeatureValue> FeatureValues { get; init; } = new();
    public Dictionary<string, object?>? Metadata { get; set; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; set; }
}

public class Plan : Entity<PlanProps>
{
    public Plan(PlanProps props, long? id = null) : base(props, id)
    {
    }

    public string ProductKey => Props.ProductKey;

    public string Key => Props.Key;

    public string DisplayName => Props.DisplayName;

    public PlanStatus Status => Props.Status;

    public void Archive()
    {
        Props.Status = PlanStatus.Archived;
        Props.UpdatedAt = DateHelper.Now();
    }

    public void Unarchive()
    {
        Props.Status = PlanStatus.Active;
        Props.UpdatedAt = DateHelper.Now();
    }

    public void SetFeatureValue(long featureId, string value)
    {
        var existing = Props.FeatureValues.Find(fv => fv.FeatureId == featureId);
        if (existing != null)
        {
            existing.Value = value;
            existing.UpdatedAt = DateHelper.Now();
        }
        else
        {
            Props.FeatureValues.Add(new PlanFeatureValue
            {
                FeatureId = featureId,
                Value = value,
                CreatedAt = DateHelper.Now(),
                UpdatedAt = DateHelper.Now()
            });
        }
        Props.UpdatedAt = DateHelper.Now();
    }

    public void RemoveFeatureValue(long featureId)
    {
        Props.FeatureValues.RemoveAll(fv => fv.FeatureId == featureId);
        Props.UpdatedAt = DateHelper.Now();
    }

    public string? GetFeatureValue(long featureId)
    {
        var featureValue = Props.FeatureValues.Find(fv => fv.FeatureId == featureId);
        return featureValue?.Value;
    }

    public bool CanDelete()
    {
        return Props.Status == PlanStatus.Archived;
    }

    public void UpdateDisplayName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length == 0)
        {
            throw new DomainException("Display name cannot be empty");
        }
        Props.DisplayName = name;
        Props.UpdatedAt = DateHelper.Now();
    }
}
