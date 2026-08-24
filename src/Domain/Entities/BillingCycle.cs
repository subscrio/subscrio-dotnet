using Subscrio.Core.Domain.Base;
using Subscrio.Core.Domain.ValueObjects;
using Subscrio.Core.Infrastructure.Utils;

namespace Subscrio.Core.Domain.Entities;

public class BillingCycleProps
{
    public required long PlanId { get; init; }
    public required string Key { get; init; }
    public required string DisplayName { get; set; }
    public string? Description { get; set; }
    public required BillingCycleStatus Status { get; set; }
    public int? DurationValue { get; set; } // Optional for forever duration
    public required DurationUnit DurationUnit { get; set; }
    public string? ExternalProductId { get; set; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; set; }
}

public class BillingCycle : Entity<BillingCycleProps>
{
    public BillingCycle(BillingCycleProps props, long? id = null) : base(props, id)
    {
    }

    public long PlanId => Props.PlanId;

    public string Key => Props.Key;

    public string DisplayName => Props.DisplayName;

    public BillingCycleStatus Status => Props.Status;

    public DateTime? CalculateNextPeriodEnd(DateTime startDate)
    {
        // For forever duration, return null (never expires)
        if (Props.DurationUnit == DurationUnit.Forever)
        {
            return null;
        }

        var nextDate = startDate;
        var durationValue = Props.DurationValue ?? 1; // Default to 1 if not specified

        nextDate = Props.DurationUnit switch
        {
            DurationUnit.Days => nextDate.AddDays(durationValue),
            DurationUnit.Weeks => nextDate.AddDays(durationValue * 7),
            DurationUnit.Months => nextDate.AddMonths(durationValue),
            DurationUnit.Years => nextDate.AddYears(durationValue),
            _ => nextDate
        };

        return nextDate;
    }

    public void Archive()
    {
        Props.Status = BillingCycleStatus.Archived;
        Props.UpdatedAt = DateHelper.Now();
    }

    public void Unarchive()
    {
        Props.Status = BillingCycleStatus.Active;
        Props.UpdatedAt = DateHelper.Now();
    }

    public bool CanDelete()
    {
        return Props.Status == BillingCycleStatus.Archived;
    }
}
