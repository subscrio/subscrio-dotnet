using System.Data.Common;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Npgsql;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Application.Errors;
using Subscrio.Core.Domain.ValueObjects;
using Subscrio.Core.Tests.Setup;

namespace Subscrio.Core.Tests.E2E;

public partial class EntitlementTests
{
    [Fact]
    public async Task ReplacementAddonsUsePriorityThenKeyAndRequireQuantityOne()
    {
        using var t = await Setup("numeric");
        foreach (var (suffix, priority, value) in new[] { ("z", 5, "40"), ("b", 1, "30"), ("a", 1, "20") })
        {
            var key = t.P + suffix;
            await t.App.Addons.CreateAddonAsync(new(key, t.P, key, CompositionMode: "override", Priority: priority));
            await t.App.Addons.UpdateAddonAsync(key, new(FeatureValues: new()
            {
                [t.F] = value
            }));
            await t.App.Subscriptions.AttachAddonAsync(t.S, key);
            await Assert.ThrowsAsync<ValidationException>(() => t.App.Subscriptions.AttachAddonAsync(t.S, key, 2));
        }
        Assert.Equal("20", await t.App.FeatureChecker.GetValueForSubscriptionAsync<string>(t.S, t.F));
        await t.App.Subscriptions.DetachAddonAsync(t.S, t.P + "a");
        Assert.Equal("30", await t.App.FeatureChecker.GetValueForSubscriptionAsync<string>(t.S, t.F));
    }

    [Fact]
    public async Task CrossSubscriptionCompositionDefaultsToMaximumAndSupportsExplicitAddition()
    {
        using var t = await Setup("numeric");
        await t.App.Subscriptions.CreateSubscriptionAsync(new(t.S + "2", t.C, t.Cycle, ActivationDate: t.Clock.UtcNow));
        Assert.Equal("10", await t.App.FeatureChecker.GetValueForCustomerAsync<string>(t.C, t.P, t.F));
        await t.App.Products.AssociateFeatureAsync(t.P, t.F, new("additive", "additive"));
        Assert.Equal("20", await t.App.FeatureChecker.GetValueForCustomerAsync<string>(t.C, t.P, t.F));
        Assert.Equal("10", await t.App.FeatureChecker.GetValueForSubscriptionAsync<string>(t.S, t.F));
    }

    [Fact]
    public async Task TimedOverridesRejectMissingAmbiguousAndNonfutureExpirations()
    {
        using var t = await Setup();
        foreach (var expiry in new DateTime?[] { null, t.Clock.UtcNow, t.Clock.UtcNow.AddTicks(-1), DateTime.SpecifyKind(t.Clock.UtcNow.AddDays(1), DateTimeKind.Unspecified) })
            await Assert.ThrowsAsync<ValidationException>(() => t.App.Subscriptions.AddFeatureOverrideAsync(t.S, t.F, "1", OverrideType.Timed, expiry));
        foreach (var type in new[] { OverrideType.Permanent, OverrideType.Temporary })
            await Assert.ThrowsAsync<ValidationException>(() => t.App.Subscriptions.AddFeatureOverrideAsync(t.S, t.F, "1", type, t.Clock.UtcNow.AddDays(1)));
        Assert.Empty((await t.App.Subscriptions.GetSubscriptionAsync(t.S))!.FeatureOverrides);
    }

    [Fact]
    public async Task ConcurrentUsageReplaysPreserveTheCompleteSnapshotAcrossReset()
    {
        using var t = await Setup();
        var options = new UsageReportOptions("same", Metadata: new()
        {
            ["source"] = "test"
        });
        var first = await t.App.Metering.ReportUsageAsync(t.C, t.P, t.F, 3, options);
        var replays = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => t.App.Metering.ReportUsageAsync(t.C, t.P, t.F, 3, options)));
        Assert.All(replays, replay => Assert.Equal(first, replay));
        Assert.Single(await t.App.Metering.ListUsageEventsAsync(t.C, t.P, t.F));
        t.Clock.UtcNow = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(0, (await t.App.Metering.GetUsageAsync(t.C, t.P, t.F)).Consumed);
        Assert.Equal(first, await t.App.Metering.ReportUsageAsync(t.C, t.P, t.F, 3, options));
        await Assert.ThrowsAsync<IdempotencyConflictException>(() => t.App.Metering.ReportUsageAsync(t.C, t.P, t.F, 4, options));
        await Assert.ThrowsAsync<IdempotencyConflictException>(() => t.App.Metering.ReportUsageAsync(t.C, t.P, t.F, 3, options with { Metadata = new() { ["source"] = "changed" } }));
    }

    [Fact]
    public async Task CountAggregationAndCustomerScopeRejectBypasses()
    {
        using var t = await Setup(config: new("monthly", "hard", "count", "customer"));
        await Assert.ThrowsAsync<ValidationException>(() => t.App.Metering.ReportUsageAsync(t.C, t.P, t.F, 2, new("count")));
        await Assert.ThrowsAsync<ValidationException>(() => t.App.Metering.GetUsageAsync(t.C, t.P, t.F, new(t.S)));
        foreach (var quantity in new long[] { -1, 0, 9007199254740992, long.MaxValue })
            await Assert.ThrowsAsync<ValidationException>(() => t.App.Metering.ReportUsageAsync(t.C, t.P, t.F, quantity, new("invalid")));
        Assert.Empty(await t.App.Metering.ListUsageEventsAsync(t.C, t.P, t.F));
        Assert.Equal(1, (await t.App.Metering.ReportUsageAsync(t.C, t.P, t.F, 1, new("invalid"))).Usage.Consumed);
    }

    [Fact]
    public async Task SubscriptionMeterRejectsAnotherCustomersSubscription()
    {
        using var t = await Setup(config: new("billing_period", "hard", "sum", "subscription"));
        await t.App.Customers.CreateCustomerAsync(new(t.C + "other"));
        await t.App.Subscriptions.CreateSubscriptionAsync(new(t.S + "other", t.C + "other", t.Cycle, ActivationDate: t.Clock.UtcNow, CurrentPeriodStart: t.Clock.UtcNow, CurrentPeriodEnd: t.Clock.UtcNow.AddMonths(1)));
        await Assert.ThrowsAsync<NotFoundException>(() => t.App.Metering.ReportUsageAsync(t.C, t.P, t.F, 1, new("foreign", t.S + "other")));
        Assert.Empty(await t.App.Metering.ListUsageEventsAsync(t.C, t.P, t.F));
    }

    [Theory]
    [InlineData("hourly", "2026-03-08T07:00:00.000Z", "2026-03-08T08:00:00.000Z")]
    [InlineData("daily", "2026-03-08T00:00:00.000Z", "2026-03-09T00:00:00.000Z")]
    [InlineData("weekly", "2026-03-02T00:00:00.000Z", "2026-03-09T00:00:00.000Z")]
    [InlineData("monthly", "2026-03-01T00:00:00.000Z", "2026-04-01T00:00:00.000Z")]
    [InlineData("yearly", "2026-01-01T00:00:00.000Z", "2027-01-01T00:00:00.000Z")]
    public async Task CalendarUsageWindowsAreUtcAndExcludeTheirEnd(string period, string start, string end)
    {
        using var t = await Setup(config: new(period, "hard", "sum", "customer"));
        t.Clock.UtcNow = new(2026, 3, 8, 7, 23, 11, DateTimeKind.Utc);
        var first = await t.App.Metering.ReportUsageAsync(t.C, t.P, t.F, 2, new("before"));
        Assert.Equal(start, first.Usage.PeriodStart);
        Assert.Equal(end, first.Usage.PeriodEnd);
        var boundary = DateTime.Parse(end, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
        t.Clock.UtcNow = boundary.AddMilliseconds(-1);
        Assert.Equal(2, (await t.App.Metering.GetUsageAsync(t.C, t.P, t.F)).Consumed);
        t.Clock.UtcNow = boundary;
        var next = await t.App.Metering.GetUsageAsync(t.C, t.P, t.F);
        Assert.Equal(end, next.PeriodStart);
        Assert.Equal(0, next.Consumed);
        Assert.Single(await t.App.Metering.ListUsageEventsAsync(t.C, t.P, t.F));
    }

    [Fact]
    public async Task YearlyGrantsReturnToTheOriginalLeapDayAnchor()
    {
        using var t = await Setup(start: new DateTime(2024, 2, 29, 12, 0, 0, DateTimeKind.Utc));
        var currency = t.C + "yearly";
        await t.App.Credits.CreateCurrencyAsync(new(currency, currency));
        await t.App.Credits.SetPlanGrantAsync(t.Plan, currency, new(10, "yearly", "grant_period_end"));
        Assert.Equal(10, (await t.App.Credits.GetBalanceAsync(t.C, currency)).Available);
        Assert.Equal("2025-02-28T12:00:00.000Z", Assert.Single(await t.App.Credits.ListGrantsAsync(t.C, currency)).ExpiresAt);
        t.Clock.UtcNow = new(2027, 2, 28, 12, 0, 0, DateTimeKind.Utc);
        await t.App.Credits.ProcessScheduledGrantsAsync(t.C);
        Assert.Equal("2028-02-29T12:00:00.000Z", (await t.App.Credits.ListGrantsAsync(t.C, currency))[0].ExpiresAt);
        t.Clock.UtcNow = new(2028, 2, 29, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(10, (await t.App.Credits.GetBalanceAsync(t.C, currency)).Available);
        Assert.Equal(5, (await t.App.Credits.ListGrantsAsync(t.C, currency)).Count);
        Assert.Equal(4, (await t.App.Credits.ListLedgerEntriesAsync(t.C, currency)).Count(e => e.Reason == "expiry"));
    }

    [Fact]
    public async Task ManualGrantExpiryPostsOneDebitAndReplayDoesNotRestoreCredits()
    {
        using var t = await Setup("numeric");
        var currency = t.C + "expiry";
        await t.App.Credits.CreateCurrencyAsync(new(currency, currency));
        await t.App.Credits.SetConsumptionRuleAsync(t.F, currency, 1);
        var input = new CreditGrantInput(t.C, currency, 10, "manual", "grant", ExpiresAt: t.Clock.UtcNow.AddDays(1));
        var first = await t.App.Credits.GrantAsync(input);
        t.Clock.UtcNow = input.ExpiresAt!.Value;
        Assert.Equal(0, (await t.App.Credits.GetBalanceAsync(t.C, currency)).Available);
        Assert.Equal(first, await t.App.Credits.GrantAsync(input));
        await Assert.ThrowsAsync<InsufficientCreditsException>(() => t.App.Credits.ConsumeAsync(new(t.C, t.F, 1, "expired")));
        Assert.Equal(0, (await t.App.Credits.GetBalanceAsync(t.C, currency)).Available);
        Assert.Single(await t.App.Credits.ListLedgerEntriesAsync(t.C, currency), e => e.Reason == "expiry");
        Assert.Null(await t.App.Credits.GetOperationAsync(t.C, "expired"));
    }

    [Fact]
    public async Task UsageHistoryPreventsSubscriptionAndCustomerDeletion()
    {
        using var t = await Setup(config: new("monthly", "hard", "sum", "subscription"));
        await t.App.Metering.ReportUsageAsync(t.C, t.P, t.F, 1, new("history", t.S));
        await Assert.ThrowsAsync<ConflictException>(() => t.App.Subscriptions.DeleteSubscriptionAsync(t.S));
        await t.App.Customers.ArchiveCustomerAsync(t.C);
        await Assert.ThrowsAsync<ConflictException>(() => t.App.Customers.DeleteCustomerAsync(t.C));
        Assert.Single(await t.App.Metering.ListUsageEventsAsync(t.C, t.P, t.F));
        Assert.NotNull(await t.App.Subscriptions.GetSubscriptionAsync(t.S));
        await t.App.Customers.UpdateCustomerAsync(t.C, new(DisplayName: "Retained customer"));
        Assert.Equal("Retained customer", (await t.App.Customers.GetCustomerAsync(t.C))!.DisplayName);
    }

    [Fact]
    public async Task HooksCannotReplaceUsageWithUnsafeAmounts()
    {
        using var t = await Setup();
        foreach (var quantity in new long[] { -1, 0, 9007199254740992 })
        {
            var off = t.App.Hooks.OnUsageReportedBefore((e, ct) => { e.Input["quantity"] = quantity; return Task.CompletedTask; });
            try
            {
                await Assert.ThrowsAsync<ValidationException>(() => t.App.Metering.ReportUsageAsync(t.C, t.P, t.F, 1, new("unsafe")));
            }
            finally { off(); }
        }
        Assert.Empty(await t.App.Metering.ListUsageEventsAsync(t.C, t.P, t.F));
        Assert.Equal(1, (await t.App.Metering.ReportUsageAsync(t.C, t.P, t.F, 1, new("unsafe"))).Usage.Consumed);
    }

    [Fact]
    public async Task InvalidOverrideRemovalRejectsBeforeAnyCatalogWrite()
    {
        using var t = await Setup("numeric");
        var currency = t.C + "invalid";
        var config = new ConfigSyncDto("1.0", [], [], CreditCurrencies: [new(currency, currency)],
            Subscriptions: [new(t.S, [new(t.F, "2", "timed", Remove: true)])]);
        await Assert.ThrowsAsync<ValidationException>(() => t.App.ConfigSync.SyncFromJsonAsync(config));
        Assert.Null(await t.App.Credits.GetCurrencyAsync(currency));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("additive", "override_wins")]
    public async Task NumericToTextConversionPreservesExportableComposition(string? crossRule, string? expectedRule)
    {
        using var t = await Setup("numeric");
        await t.App.Products.AssociateFeatureAsync(t.P, t.F, new("additive", crossRule));
        await t.App.Features.UpdateFeatureAsync(t.F, new(ValueType: "text", DefaultValue: "standard"));
        Assert.Equal(new("override_wins", expectedRule), (await t.App.Products.GetProductAsync(t.P))!.Features.Single(f => f.FeatureKey == t.F).Resolution);
        var exported = await t.App.ConfigSync.ExportConfigAsync();
        Assert.Empty((await t.App.ConfigSync.SyncFromJsonAsync(exported)).Errors);
    }

    private static async Task<DbConnection> OpenFixtureConnection(Fixture t)
    {
        DbConnection connection = t.SqlDatabase == null
            ? new NpgsqlConnection(TestDatabaseAssemblyFixture.GetTestConnectionString())
            : new SqlConnection($"Server=localhost;Database={t.SqlDatabase};Integrated Security=True;Encrypt=True;TrustServerCertificate=True");
        await connection.OpenAsync();
        return connection;
    }

    private static async Task ExecuteSql(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DatabaseInsertFailureRollsBackAccountingAndAllowsSameKeyRetry(bool credit)
    {
        using var t = await Setup(credit ? "numeric" : "metered");
        var currency = t.C + "fault";
        if (credit)
        {
            await t.App.Credits.CreateCurrencyAsync(new(currency, currency));
            await t.App.Credits.SetConsumptionRuleAsync(t.F, currency, 2);
            await t.App.Credits.GrantAsync(new(t.C, currency, 10, "manual", "fund"));
        }
        else
            await t.App.Metering.ReportUsageAsync(t.C, t.P, t.F, 2, new("seed"));

        await using var connection = await OpenFixtureConnection(t);
        var name = "fail_" + Guid.NewGuid().ToString("N");
        var table = credit ? "credit_ledger_entries" : "usage_events";
        var predicate = credit ? "reason = 'consumption'" : "idempotency_key = 'injected-failure'";
        var sqlServer = t.SqlDatabase != null;
        if (sqlServer)
            await ExecuteSql(connection, $"ALTER TABLE subscrio.{table} ADD CONSTRAINT {name} CHECK (NOT ({predicate}))");
        else
        {
            await ExecuteSql(connection, $"CREATE FUNCTION subscrio.{name}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.{predicate} THEN RAISE EXCEPTION 'injected accounting failure'; END IF; RETURN NEW; END $$");
            await ExecuteSql(connection, $"CREATE TRIGGER {name} BEFORE INSERT ON subscrio.{table} FOR EACH ROW EXECUTE FUNCTION subscrio.{name}()");
        }
        try
        {
            var error = await Record.ExceptionAsync(async () =>
            {
                if (credit)
                    await t.App.Credits.ConsumeAsync(new(t.C, t.F, 3, "injected-failure"));
                else
                    await t.App.Metering.ReportUsageAsync(t.C, t.P, t.F, 3, new("injected-failure"));
            });
            Assert.NotNull(error);
            Assert.Contains(sqlServer ? name : "injected accounting failure", error.ToString());
            if (credit)
            {
                Assert.Equal(10, (await t.App.Credits.GetBalanceAsync(t.C, currency)).Available);
                Assert.Equal(10, Assert.Single(await t.App.Credits.ListGrantsAsync(t.C, currency)).RemainingAmount);
                Assert.Null(await t.App.Credits.GetOperationAsync(t.C, "injected-failure"));
                Assert.Single(await t.App.Credits.ListLedgerEntriesAsync(t.C, currency));
            }
            else
            {
                Assert.Equal(2, (await t.App.Metering.GetUsageAsync(t.C, t.P, t.F)).Consumed);
                Assert.Single(await t.App.Metering.ListUsageEventsAsync(t.C, t.P, t.F));
            }
        }
        finally
        {
            if (sqlServer)
                await ExecuteSql(connection, $"ALTER TABLE subscrio.{table} DROP CONSTRAINT {name}");
            else
            {
                await ExecuteSql(connection, $"DROP TRIGGER {name} ON subscrio.{table}");
                await ExecuteSql(connection, $"DROP FUNCTION subscrio.{name}()");
            }
        }
        if (credit)
            Assert.Equal(4, Assert.Single((await t.App.Credits.ConsumeAsync(new(t.C, t.F, 3, "injected-failure"))).Balances).Available);
        else
            Assert.Equal(5, (await t.App.Metering.ReportUsageAsync(t.C, t.P, t.F, 3, new("injected-failure"))).Usage.Consumed);
    }
}
