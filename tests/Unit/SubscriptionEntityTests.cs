using FluentAssertions;
using Subscrio.Core.Domain.Entities;
using Subscrio.Core.Domain.Errors;
using Subscrio.Core.Domain.ValueObjects;
using Xunit;

namespace Subscrio.Core.Tests.Unit;

public class SubscriptionEntityTests
{
    [Fact]
    public void Activate_SetsActivationDate_AndClearsTrialEndDate()
    {
        var subscription = CreateSubscription(status: SubscriptionStatus.Trial, trialEndDate: DateTime.UtcNow.AddDays(3));

        subscription.Activate();

        subscription.Props.ActivationDate.Should().NotBeNull();
        subscription.Props.TrialEndDate.Should().BeNull();
    }

    [Fact]
    public void Cancel_SetsCancellationDate()
    {
        var subscription = CreateSubscription(status: SubscriptionStatus.Active);

        subscription.Cancel();

        subscription.Props.CancellationDate.Should().NotBeNull();
    }

    [Fact]
    public void Cancel_Throws_WhenAlreadyCancelled()
    {
        var subscription = CreateSubscription(status: SubscriptionStatus.Cancelled);

        Action act = () => subscription.Cancel();
        act.Should().Throw<DomainException>().WithMessage("*already cancelled*");
    }

    [Fact]
    public void Expire_SetsExpirationDate()
    {
        var subscription = CreateSubscription(status: SubscriptionStatus.Active);

        subscription.Expire();

        subscription.Props.ExpirationDate.Should().NotBeNull();
    }

    [Fact]
    public void Archive_And_Unarchive_ToggleIsArchived()
    {
        var subscription = CreateSubscription(status: SubscriptionStatus.Active);

        subscription.Archive();
        subscription.IsArchived.Should().BeTrue();

        subscription.Unarchive();
        subscription.IsArchived.Should().BeFalse();
    }

    [Fact]
    public void MarkAsTransitioned_ArchivesAndSetsTransitionedAt()
    {
        var subscription = CreateSubscription(status: SubscriptionStatus.Active);

        subscription.MarkAsTransitioned();

        subscription.IsArchived.Should().BeTrue();
        subscription.Props.TransitionedAt.Should().NotBeNull();
    }

    [Fact]
    public void Renew_ClearsTemporaryOverrides_KeepsPermanent()
    {
        var subscription = CreateSubscription(status: SubscriptionStatus.Active);
        subscription.AddFeatureOverride(1, "temp", OverrideType.Temporary);
        subscription.AddFeatureOverride(2, "perm", OverrideType.Permanent);

        subscription.Renew();

        subscription.GetFeatureOverride(1).Should().BeNull();
        subscription.GetFeatureOverride(2).Should().NotBeNull();
        subscription.GetFeatureOverride(2)!.Value.Should().Be("perm");
    }

    [Fact]
    public void FeatureOverride_AddReplaceRemove_Works()
    {
        var subscription = CreateSubscription(status: SubscriptionStatus.Active);

        subscription.AddFeatureOverride(10, "first", OverrideType.Permanent);
        subscription.AddFeatureOverride(10, "second", OverrideType.Permanent);

        subscription.GetFeatureOverride(10)!.Value.Should().Be("second");
        subscription.Props.FeatureOverrides.Should().HaveCount(1);

        subscription.RemoveFeatureOverride(10);
        subscription.GetFeatureOverride(10).Should().BeNull();
    }

    [Fact]
    public void SetPeriodAndDates_UpdateProps()
    {
        var subscription = CreateSubscription(status: SubscriptionStatus.Pending);
        var activation = DateTime.UtcNow.AddDays(-1);
        var expiration = DateTime.UtcNow.AddMonths(1);
        var periodStart = DateTime.UtcNow;
        var periodEnd = periodStart.AddMonths(1);

        subscription.SetActivationDate(activation);
        subscription.SetExpirationDate(expiration);
        subscription.SetTrialEndDate(periodStart.AddDays(7));
        subscription.SetCurrentPeriod(periodStart, periodEnd);

        subscription.Props.ActivationDate.Should().Be(activation);
        subscription.Props.ExpirationDate.Should().Be(expiration);
        subscription.Props.TrialEndDate.Should().NotBeNull();
        subscription.Props.CurrentPeriodStart.Should().Be(periodStart);
        subscription.Props.CurrentPeriodEnd.Should().Be(periodEnd);
        subscription.CanDelete().Should().BeTrue();
    }

    private static Subscription CreateSubscription(
        SubscriptionStatus status,
        DateTime? trialEndDate = null)
    {
        var now = DateTime.UtcNow;
        return new Subscription(new SubscriptionProps
        {
            Key = $"sub-{Guid.NewGuid():N}",
            CustomerId = 1,
            PlanId = 1,
            BillingCycleId = 1,
            Status = status,
            IsArchived = false,
            ActivationDate = status == SubscriptionStatus.Pending ? null : now.AddDays(-1),
            TrialEndDate = trialEndDate,
            FeatureOverrides = new List<FeatureOverride>(),
            CreatedAt = now,
            UpdatedAt = now
        });
    }
}
