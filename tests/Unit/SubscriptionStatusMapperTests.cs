using FluentAssertions;
using Subscrio.Core.Application.Mappers;
using Subscrio.Core.Domain.ValueObjects;
using Xunit;

namespace Subscrio.Core.Tests.Unit;

public class SubscriptionStatusMapperTests
{
    [Fact]
    public void ParseStatus_MapsCancellationPending()
    {
        SubscriptionMapper.ParseStatus("cancellation_pending")
            .Should().Be(SubscriptionStatus.CancellationPending);
    }

    [Fact]
    public void FormatStatus_FormatsCancellationPending()
    {
        SubscriptionMapper.FormatStatus(SubscriptionStatus.CancellationPending)
            .Should().Be("cancellation_pending");
    }

    [Fact]
    public void ParseStatus_AndFormatStatus_RoundTripCancellationPending()
    {
        var parsed = SubscriptionMapper.ParseStatus("cancellation_pending");
        SubscriptionMapper.FormatStatus(parsed).Should().Be("cancellation_pending");
    }
}
