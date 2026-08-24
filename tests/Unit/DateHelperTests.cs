using FluentAssertions;
using Subscrio.Core.Infrastructure.Utils;
using Xunit;

namespace Subscrio.Core.Tests.Unit;

public class DateHelperTests
{
    [Fact]
    public void Now_ReturnsCurrentDate()
    {
        var current = DateHelper.Now();
        current.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void NowISO_ReturnsCurrentDateAsISOString()
    {
        var iso = DateHelper.NowISO();
        iso.Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}Z$");
    }

    [Fact]
    public void FromISOString_CreatesDateFromValidISOString()
    {
        var date = DateHelper.FromISOString("2023-12-25T10:30:00.000Z");
        date.Year.Should().Be(2023);
        date.Month.Should().Be(12);
        date.Day.Should().Be(25);
    }

    [Fact]
    public void FromISOString_ThrowsErrorForInvalidISOString()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            DateHelper.FromISOString("invalid-date");
        });
    }

    [Fact]
    public void AddDays_AddsDaysToDate()
    {
        var date = new DateTime(2023, 12, 25, 10, 30, 0, DateTimeKind.Utc);
        var result = DateHelper.AddDays(date, 5);
        result.Day.Should().Be(30);
    }

    [Fact]
    public void AddDays_HandlesNegativeDays()
    {
        var date = new DateTime(2023, 12, 25, 10, 30, 0, DateTimeKind.Utc);
        var result = DateHelper.AddDays(date, -5);
        result.Day.Should().Be(20);
    }

    [Fact]
    public void AddMonths_AddsMonthsToDate()
    {
        var date = new DateTime(2023, 12, 25, 10, 30, 0, DateTimeKind.Utc);
        var result = DateHelper.AddMonths(date, 2);
        result.Month.Should().Be(2);
        result.Year.Should().Be(2024);
    }

    [Fact]
    public void AddYears_AddsYearsToDate()
    {
        var date = new DateTime(2023, 12, 25, 10, 30, 0, DateTimeKind.Utc);
        var result = DateHelper.AddYears(date, 1);
        result.Year.Should().Be(2024);
    }

    [Fact]
    public void IsPast_ReturnsTrueForPastDate()
    {
        var pastDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        DateHelper.IsPast(pastDate).Should().BeTrue();
    }

    [Fact]
    public void IsPast_ReturnsFalseForFutureDate()
    {
        var futureDate = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        DateHelper.IsPast(futureDate).Should().BeFalse();
    }

    [Fact]
    public void IsFuture_ReturnsTrueForFutureDate()
    {
        var futureDate = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        DateHelper.IsFuture(futureDate).Should().BeTrue();
    }

    [Fact]
    public void IsFuture_ReturnsFalseForPastDate()
    {
        var pastDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        DateHelper.IsFuture(pastDate).Should().BeFalse();
    }

    [Fact]
    public void IsToday_ReturnsTrueForToday()
    {
        var today = DateTime.UtcNow;
        DateHelper.IsToday(today).Should().BeTrue();
    }

    [Fact]
    public void IsToday_ReturnsFalseForYesterday()
    {
        var yesterday = DateHelper.AddDays(DateTime.UtcNow, -1);
        DateHelper.IsToday(yesterday).Should().BeFalse();
    }

    [Fact]
    public void FormatDate_FormatsDateForDisplay()
    {
        var date = new DateTime(2023, 12, 25, 10, 30, 0, DateTimeKind.Utc);
        var formatted = DateHelper.FormatDate(date);
        formatted.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void FormatDateTime_FormatsDateAndTimeForDisplay()
    {
        var date = new DateTime(2023, 12, 25, 10, 30, 0, DateTimeKind.Utc);
        var formatted = DateHelper.FormatDateTime(date);
        formatted.Should().NotBeNullOrEmpty();
    }
}


