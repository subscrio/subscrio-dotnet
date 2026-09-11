using FluentAssertions;
using Subscrio.Core.Domain.Entities;
using Subscrio.Core.Domain.Errors;
using Subscrio.Core.Domain.ValueObjects;
using Xunit;

namespace Subscrio.Core.Tests.Unit;

public class PlanEntityTests
{
    [Fact]
    public void Archive_SetsStatusToArchived()
    {
        var plan = CreatePlan();

        plan.Archive();

        plan.Status.Should().Be(PlanStatus.Archived);
        plan.Props.Status.Should().Be(PlanStatus.Archived);
    }

    [Fact]
    public void Unarchive_SetsStatusToActive()
    {
        var plan = CreatePlan(status: PlanStatus.Archived);

        plan.Unarchive();

        plan.Status.Should().Be(PlanStatus.Active);
    }

    [Fact]
    public void CanDelete_True_WhenArchived()
    {
        var plan = CreatePlan(status: PlanStatus.Archived);

        plan.CanDelete().Should().BeTrue();
    }

    [Fact]
    public void CanDelete_False_WhenActive()
    {
        var plan = CreatePlan(status: PlanStatus.Active);

        plan.CanDelete().Should().BeFalse();
    }

    [Fact]
    public void UpdateDisplayName_UpdatesName()
    {
        var plan = CreatePlan();

        plan.UpdateDisplayName("Renamed Plan");

        plan.DisplayName.Should().Be("Renamed Plan");
        plan.Props.DisplayName.Should().Be("Renamed Plan");
    }

    [Fact]
    public void UpdateDisplayName_Throws_WhenEmpty()
    {
        var plan = CreatePlan();

        Action act = () => plan.UpdateDisplayName("   ");
        act.Should().Throw<DomainException>().WithMessage("*cannot be empty*");
    }

    [Fact]
    public void SetFeatureValue_AddsAndUpdates()
    {
        var plan = CreatePlan();

        plan.SetFeatureValue(10, "first");
        plan.GetFeatureValue(10).Should().Be("first");
        plan.Props.FeatureValues.Should().HaveCount(1);

        plan.SetFeatureValue(10, "second");
        plan.GetFeatureValue(10).Should().Be("second");
        plan.Props.FeatureValues.Should().HaveCount(1);
    }

    [Fact]
    public void RemoveFeatureValue_RemovesExisting()
    {
        var plan = CreatePlan();
        plan.SetFeatureValue(7, "value");

        plan.RemoveFeatureValue(7);

        plan.GetFeatureValue(7).Should().BeNull();
        plan.Props.FeatureValues.Should().BeEmpty();
    }

    [Fact]
    public void GetFeatureValue_ReturnsNull_WhenMissing()
    {
        var plan = CreatePlan();

        plan.GetFeatureValue(99).Should().BeNull();
    }

    private static Plan CreatePlan(PlanStatus status = PlanStatus.Active)
    {
        var now = DateTime.UtcNow;
        return new Plan(new PlanProps
        {
            ProductKey = $"product-{Guid.NewGuid():N}",
            Key = $"plan-{Guid.NewGuid():N}",
            DisplayName = "Test Plan",
            Status = status,
            FeatureValues = new List<PlanFeatureValue>(),
            CreatedAt = now,
            UpdatedAt = now
        });
    }
}
