using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Application.Errors;
using Subscrio.Core.Config;
using Subscrio.Core.Domain.ValueObjects;
using Subscrio.Core.Tests.Setup;

namespace Subscrio.Core.Tests.E2E;

public partial class EntitlementTests
{
    private sealed class Clock : IClock
    {
        public DateTime UtcNow { get; set; } = DateTime.Parse("2026-01-31T12:00:00Z").ToUniversalTime();
    }
    private sealed record Fixture(Subscrio App, Clock Clock, string P, string F, string Plan, string Cycle, string C, string S, string? SqlDatabase = null) : IDisposable
    {
        public void Dispose()
        {
            App.Dispose();
            if (SqlDatabase != null)
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(SqlDatabase, "^SubscrioEntitlementsTest_[a-f0-9]{32}$"))
                    throw new InvalidOperationException("Refusing to drop an unexpected database name");
                Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
                using var admin = new Microsoft.Data.SqlClient.SqlConnection("Server=localhost;Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=True");
                admin.Open();
                using var command = admin.CreateCommand();
                command.CommandText = $"ALTER DATABASE [{SqlDatabase}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{SqlDatabase}]";
                command.ExecuteNonQuery();
            }
        }
    }
    private static async Task<Fixture> Setup(string type = "metered", MeteredFeatureConfigDto? config = null, DateTime? start = null)
    {
        var sqlServer = Environment.GetEnvironmentVariable("SUBSCRIO_ENTITLEMENT_SQLSERVER") == "1";
        var clock = new Clock();
        if (start.HasValue)
            clock.UtcNow = start.Value;
        var key = "netent-" + Guid.NewGuid().ToString("N");
        DatabaseConfig database;
        if (sqlServer)
        {
            var databaseName = "SubscrioEntitlementsTest_" + Guid.NewGuid().ToString("N");
            await using var admin = new Microsoft.Data.SqlClient.SqlConnection("Server=localhost;Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=True");
            await admin.OpenAsync();
            await using var command = admin.CreateCommand();
            command.CommandText = $"CREATE DATABASE [{databaseName}]";
            await command.ExecuteNonQueryAsync();
            database = new()
            {
                ConnectionString = $"Server=localhost;Database={databaseName};Integrated Security=True;Encrypt=True;TrustServerCertificate=True",
                DatabaseType = DatabaseType.SqlServer
            };
        }
        else
        {
            TestDatabaseAssemblyFixture.EnsureInitialized();
            database = new()
            {
                ConnectionString = TestDatabaseAssemblyFixture.GetTestConnectionString()
            };
        }
        var app = new Subscrio(new()
        {
            Database = database,
            Clock = clock
        });
        if (sqlServer)
            await app.InstallSchemaAsync();
        var t = new Fixture(app, clock, key + "p", key + "f", key + "plan", key + "cycle", key + "c", key + "s", sqlServer ? new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(database.ConnectionString).InitialCatalog : null);
        await app.Products.CreateProductAsync(new(t.P, t.P));
        await app.Features.CreateFeatureAsync(new(t.F, t.F, type, "0", MeteredConfig: type == "metered" ? config ?? new("monthly", "hard", "sum", "customer") : null));
        await app.Products.AssociateFeatureAsync(t.P, t.F);
        await app.Plans.CreatePlanAsync(new(t.P, t.Plan, t.Plan));
        await app.Plans.SetFeatureValueAsync(t.Plan, t.F, "10");
        await app.BillingCycles.CreateBillingCycleAsync(new(t.Plan, t.Cycle, t.Cycle, "months", DurationValue: 1));
        await app.Customers.CreateCustomerAsync(new(t.C));
        await app.Subscriptions.CreateSubscriptionAsync(new(t.S, t.C, t.Cycle, ActivationDate: clock.UtcNow, CurrentPeriodStart: clock.UtcNow, CurrentPeriodEnd: clock.UtcNow.AddMonths(1)));
        return t;
    }
    [Fact]
    public async Task AddonsAndTimedOverridesUseExistingGetters()
    {
        using var t = await Setup("numeric");
        await t.App.Addons.CreateAddonAsync(new(t.P + "addon", t.P, "Extra"));
        await t.App.Addons.UpdateAddonAsync(t.P + "addon", new(FeatureValues: new()
        {
            [t.F] = "3"
        }));
        await t.App.Subscriptions.AttachAddonAsync(t.S, t.P + "addon", 2);
        Assert.Equal("16", await t.App.FeatureChecker.GetValueForCustomerAsync<string>(t.C, t.P, t.F));
        await t.App.Subscriptions.AttachAddonAsync(t.S, t.P + "addon", 3);
        Assert.Equal("19", await t.App.FeatureChecker.GetValueForSubscriptionAsync<string>(t.S, t.F));
        await t.App.Subscriptions.AddFeatureOverrideAsync(t.S, t.F, "5", OverrideType.Timed, t.Clock.UtcNow.AddDays(1));
        Assert.Equal("5", await t.App.FeatureChecker.GetValueForSubscriptionAsync<string>(t.S, t.F));
        await t.App.Subscriptions.ClearTemporaryOverridesAsync(t.S);
        Assert.Single((await t.App.Subscriptions.GetSubscriptionAsync(t.S))!.FeatureOverrides);
        t.Clock.UtcNow = t.Clock.UtcNow.AddDays(1);
        Assert.Equal("19", await t.App.FeatureChecker.GetValueForCustomerAsync<string>(t.C, t.P, t.F));
        Assert.Contains((await t.App.FeatureChecker.ExplainForSubscriptionAsync(t.S, t.F)).Subscriptions[0].Sources, s => s.Reason == "Expired");
        await t.App.Addons.ArchiveAddonAsync(t.P + "addon");
        Assert.Equal("19", await t.App.FeatureChecker.GetValueForSubscriptionAsync<string>(t.S, t.F));
        await Assert.ThrowsAsync<ValidationException>(() => t.App.Subscriptions.AttachAddonAsync(t.S, t.P + "addon"));
        await t.App.Subscriptions.DetachAddonAsync(t.S, t.P + "addon");
        Assert.Equal("10", await t.App.FeatureChecker.GetValueForSubscriptionAsync<string>(t.S, t.F));
        await Assert.ThrowsAsync<ConflictException>(() => t.App.Addons.DeleteAddonAsync(t.P + "addon"));
    }
    [Fact]
    public async Task HardQuotaIsAtomicAndRetriesAreStable()
    {
        using var t = await Setup();
        var tasks = Enumerable.Range(0, 20).Select(async i => { try { await t.App.Metering.ReportUsageAsync(t.C, t.P, t.F, 1, new("request" + i)); return true; } catch (UsageLimitExceededException) { return false; } });
        Assert.Equal(10, (await Task.WhenAll(tasks)).Count(x => x));
        Assert.Equal(10, (await t.App.Metering.GetUsageAsync(t.C, t.P, t.F)).Consumed);
        var history = await t.App.Metering.ListUsageEventsAsync(t.C, t.P, t.F);
        Assert.Equal(10, history.Count);
        await Assert.ThrowsAsync<UsageLimitExceededException>(() => t.App.Metering.ReportUsageAsync(t.C, t.P, t.F, 1, new("rejected")));
        await t.App.Plans.SetFeatureValueAsync(t.Plan, t.F, "12");
        Assert.Equal(11, (await t.App.Metering.ReportUsageAsync(t.C, t.P, t.F, 1, new("rejected"))).Usage.Consumed);
        var old = history[0];
        t.Clock.UtcNow = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(0, (await t.App.Metering.GetUsageAsync(t.C, t.P, t.F)).Consumed);
        Assert.Equal(old, await t.App.Metering.ReportUsageAsync(t.C, t.P, t.F, 1, new(old.IdempotencyKey)));
        await Assert.ThrowsAsync<IdempotencyConflictException>(() => t.App.Metering.ReportUsageAsync(t.C, t.P, t.F, 2, new(old.IdempotencyKey)));
    }
    [Fact]
    public async Task SoftLimitsAndScopeValidation()
    {
        using var t = await Setup(config: new("monthly", "soft", "sum", "customer"));
        var result = await t.App.Metering.ReportUsageAsync(t.C, t.P, t.F, 15, new("soft"));
        Assert.True(result.Usage.IsOverage);
        Assert.True(result.Usage.HasAccess);
        await t.App.Plans.SetFeatureValueAsync(t.Plan, t.F, "20");
        Assert.Equal(5, (await t.App.Metering.GetUsageAsync(t.C, t.P, t.F)).Remaining);
        await Assert.ThrowsAsync<ValidationException>(() => t.App.Metering.GetUsageAsync(t.C, t.P, t.F, new(t.S)));
        await Assert.ThrowsAsync<ValidationException>(() => t.App.Features.UpdateFeatureAsync(t.F, new(MeteredConfig: new("daily", "soft", "sum", "customer"))));
    }
    [Fact]
    public async Task BillingPeriodsCannotInventAReset()
    {
        using var t = await Setup(config: new("billing_period", "hard", "sum", "subscription"));
        await Assert.ThrowsAsync<ValidationException>(() => t.App.Metering.GetUsageAsync(t.C, t.P, t.F));
        Assert.Equal("2026-02-28T12:00:00.000Z", (await t.App.Metering.GetUsageAsync(t.C, t.P, t.F, new(t.S))).PeriodEnd);
        t.Clock.UtcNow = t.Clock.UtcNow.AddMonths(1);
        await Assert.ThrowsAsync<MeteringPeriodException>(() => t.App.Metering.GetUsageAsync(t.C, t.P, t.F, new(t.S)));
    }
    [Fact]
    public async Task CreditsCannotOverdrawAndLedgerBalances()
    {
        using var t = await Setup("numeric");
        var cu = t.C + "credit";
        await t.App.Credits.CreateCurrencyAsync(new(cu, cu));
        await t.App.Credits.SetConsumptionRuleAsync(t.F, cu, 3);
        var first = await t.App.Credits.GrantAsync(new(t.C, cu, 10, "promotional", "grant", ExpiresAt: t.Clock.UtcNow.AddDays(1)));
        await t.App.Credits.GrantAsync(new(t.C, cu, 20, "prepaid", "paid", Priority: 1));
        var burn = await t.App.Credits.ConsumeAsync(new(t.C, t.F, 4, "burn"));
        Assert.Equal(first.Id, burn.Allocations[0].GrantId);
        Assert.Equal(10, burn.Allocations[0].Amount);
        Assert.Equal(2, burn.Allocations.Count);
        Assert.Equal(2, burn.Allocations[1].Amount);
        var tasks = Enumerable.Range(0, 10).Select(async i => { try { await t.App.Credits.ConsumeAsync(new(t.C, t.F, 1, "spend" + i)); return true; } catch (InsufficientCreditsException) { return false; } });
        Assert.Equal(6, (await Task.WhenAll(tasks)).Count(x => x));
        Assert.Equal(0, (await t.App.Credits.GetBalanceAsync(t.C, cu)).Available);
        Assert.Equal(0, (await t.App.Credits.ListLedgerEntriesAsync(t.C, cu)).Sum(e => e.Amount));
        var replay = await t.App.Credits.ConsumeAsync(new(t.C, t.F, 4, "burn"));
        Assert.Equal(burn.OperationId, replay.OperationId);
        Assert.Equal(burn.Allocations, replay.Allocations);
        Assert.Equal(burn.Balances, replay.Balances);
    }
    [Fact]
    public async Task MultiCurrencyFailureRollsBackAllWallets()
    {
        using var t = await Setup("numeric");
        foreach (var (cu, amount) in new[] { (t.C + "a", 10), (t.C + "b", 2) })
        {
            await t.App.Credits.CreateCurrencyAsync(new(cu, cu));
            await t.App.Credits.SetConsumptionRuleAsync(t.F, cu, 3);
            await t.App.Credits.GrantAsync(new(t.C, cu, amount, "manual", cu));
        }
        await Assert.ThrowsAsync<InsufficientCreditsException>(() => t.App.Credits.ConsumeAsync(new(t.C, t.F, 1, "multi")));
        Assert.Equal(10, (await t.App.Credits.GetBalanceAsync(t.C, t.C + "a")).Available);
        Assert.Null(await t.App.Credits.GetOperationAsync(t.C, "multi"));
    }
    [Fact]
    public async Task RecurringGrantsClampOriginalAnchorAndExpireOnce()
    {
        using var t = await Setup();
        var cu = t.C + "credit";
        await t.App.Credits.CreateCurrencyAsync(new(cu, cu));
        await t.App.Credits.SetPlanGrantAsync(t.Plan, cu, new(10, "monthly", "grant_period_end"));
        Assert.Equal(10, (await t.App.Credits.GetBalanceAsync(t.C, cu)).Available);
        t.Clock.UtcNow = new DateTime(2026, 3, 31, 12, 0, 0, DateTimeKind.Utc);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => t.App.Credits.ProcessScheduledGrantsAsync(t.C)));
        var grants = await t.App.Credits.ListGrantsAsync(t.C, cu);
        Assert.Equal(3, grants.Count);
        Assert.Equal("2026-04-30T12:00:00.000Z", grants[0].ExpiresAt);
        Assert.Equal(new[] { "2026-04-30T12:00:00.000Z", "2026-03-31T12:00:00.000Z", "2026-02-28T12:00:00.000Z" }, grants.Select(g => g.ExpiresAt));
        Assert.Equal(10, (await t.App.Credits.GetBalanceAsync(t.C, cu)).Available);
        Assert.Equal(2, (await t.App.Credits.ListLedgerEntriesAsync(t.C, cu)).Count(x => x.Reason == "expiry"));
    }
    [Fact]
    public async Task OnceGrantAndAdjustmentRemainAuditable()
    {
        using var t = await Setup();
        var cu = t.C + "credit";
        await t.App.Credits.CreateCurrencyAsync(new(cu, cu));
        await t.App.Credits.SetPlanGrantAsync(t.Plan, cu, new(10, "once"));
        await t.App.Credits.ProcessScheduledGrantsAsync(t.C);
        t.Clock.UtcNow = t.Clock.UtcNow.AddYears(1);
        await t.App.Credits.ProcessScheduledGrantsAsync(t.C);
        Assert.Single(await t.App.Credits.ListGrantsAsync(t.C, cu));
        await t.App.Credits.AdjustAsync(new(t.C, cu, -3, "correction", "adjust"));
        Assert.Equal(7, (await t.App.Credits.GetBalanceAsync(t.C, cu)).Available);
        Assert.Equal(7, (await t.App.Credits.ListLedgerEntriesAsync(t.C, cu)).Sum(e => e.Amount));
    }
    [Fact]
    public async Task SchemaMigrationIsRepeatable()
    {
        using var t = await Setup();
        Assert.Equal("1.4.0", await t.App.VerifySchemaAsync());
        Assert.Equal(0, await t.App.MigrateAsync());
        await using var connection = await OpenFixtureConnection(t);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema='subscrio'";
        await using var reader = await command.ExecuteReaderAsync();
        var tables = new HashSet<string>();
        while (await reader.ReadAsync())
            tables.Add(reader.GetString(0));
        foreach (var table in new[] { "addons", "usage_events", "credit_grants", "credit_ledger_entries", "subscription_credit_grant_states" })
            Assert.Contains(table, tables);
    }
    [Fact]
    public async Task MeteredFeaturesCannotChargeCredits()
    {
        using var t = await Setup();
        var cu = t.C + "credit";
        await t.App.Credits.CreateCurrencyAsync(new(cu, cu));
        await Assert.ThrowsAsync<ValidationException>(() => t.App.Credits.SetConsumptionRuleAsync(t.F, cu, 1));
    }
    [Fact]
    public async Task BeforeVetoRollsBackUsageAndAllowsRetry()
    {
        using var t = await Setup();
        var off = t.App.Hooks.OnUsageReportedBefore((e, ct) => throw new InvalidOperationException("veto"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => t.App.Metering.ReportUsageAsync(t.C, t.P, t.F, 2, new("veto")));
        Assert.Empty(await t.App.Metering.ListUsageEventsAsync(t.C, t.P, t.F));
        off();
        Assert.Equal(2, (await t.App.Metering.ReportUsageAsync(t.C, t.P, t.F, 2, new("veto"))).Usage.Consumed);
    }
    [Fact]
    public async Task AfterFailureIsCommittedAndRetryDoesNotEmitAgain()
    {
        using var t = await Setup();
        var calls = 0;
        t.App.Hooks.OnUsageReportedAfter((e, ct) => { calls++; throw new InvalidOperationException("delivery"); });
        var ex = await Assert.ThrowsAsync<global::Subscrio.Core.Application.Hooks.CommittedOperationHookException>(() => t.App.Metering.ReportUsageAsync(t.C, t.P, t.F, 2, new("after")));
        Assert.IsType<UsageReportDto>(ex.Result);
        Assert.Equal(2, (await t.App.Metering.ReportUsageAsync(t.C, t.P, t.F, 2, new("after"))).Quantity);
        Assert.Equal(1, calls);
    }
    [Fact]
    public async Task HookCannotChangeIdentity()
    {
        using var t = await Setup();
        t.App.Hooks.OnUsageReportedBefore((e, ct) => { e.Input["customerKey"] = "other"; return Task.CompletedTask; });
        await Assert.ThrowsAsync<ValidationException>(() => t.App.Metering.ReportUsageAsync(t.C, t.P, t.F, 1, new("identity")));
        Assert.Empty(await t.App.Metering.ListUsageEventsAsync(t.C, t.P, t.F));
    }
    [Fact]
    public async Task CatalogEditsSettleOldWindows()
    {
        using var t = await Setup();
        var cu = t.C + "credit";
        await t.App.Credits.CreateCurrencyAsync(new(cu, cu));
        await t.App.Credits.SetPlanGrantAsync(t.Plan, cu, new(10, "monthly"));
        t.Clock.UtcNow = new(2026, 3, 31, 12, 0, 0, DateTimeKind.Utc);
        await t.App.Credits.SetPlanGrantAsync(t.Plan, cu, new(20, "monthly"));
        Assert.Equal(30, (await t.App.Credits.GetBalanceAsync(t.C, cu)).Available);
        t.Clock.UtcNow = new(2026, 4, 30, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(50, (await t.App.Credits.GetBalanceAsync(t.C, cu)).Available);
    }
    [Fact]
    public async Task BackdatedCancellationSettlesOnlyEarlierPeriods()
    {
        using var t = await Setup();
        var cu = t.C + "credit";
        await t.App.Credits.CreateCurrencyAsync(new(cu, cu));
        await t.App.Credits.SetPlanGrantAsync(t.Plan, cu, new(10, "monthly"));
        t.Clock.UtcNow = new(2026, 4, 30, 12, 0, 0, DateTimeKind.Utc);
        await t.App.Subscriptions.UpdateSubscriptionAsync(t.S, new(CancellationDate: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(20, (await t.App.Credits.GetBalanceAsync(t.C, cu)).Available);
    }
    [Fact]
    public async Task ResumeDoesNotGrantArchivedPeriods()
    {
        using var t = await Setup();
        var cu = t.C + "credit";
        await t.App.Credits.CreateCurrencyAsync(new(cu, cu));
        await t.App.Credits.SetPlanGrantAsync(t.Plan, cu, new(10, "monthly"));
        await t.App.Subscriptions.ArchiveSubscriptionAsync(t.S);
        t.Clock.UtcNow = new(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        await t.App.Subscriptions.UnarchiveSubscriptionAsync(t.S);
        Assert.Equal(10, (await t.App.Credits.GetBalanceAsync(t.C, cu)).Available);
        t.Clock.UtcNow = new(2026, 4, 30, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(20, (await t.App.Credits.GetBalanceAsync(t.C, cu)).Available);
    }
    [Fact]
    public async Task BillingRenewalEmitsOnlyAfterCommit()
    {
        using var t = await Setup();
        var cu = t.C + "credit";
        await t.App.Credits.CreateCurrencyAsync(new(cu, cu));
        await t.App.Credits.SetPlanGrantAsync(t.Plan, cu, new(10, "billing_period"));
        var calls = 0;
        t.App.Hooks.OnCreditGrantedAfter(async (e, ct) => { if (e.Input["grantType"]?.GetValue<string>() == "recurring") { Assert.NotNull(await t.App.Credits.GetOperationAsync(t.C, e.Input["idempotencyKey"]!.GetValue<string>())); calls++; } });
        t.Clock.UtcNow = new(2026, 2, 28, 12, 0, 0, DateTimeKind.Utc);
        await t.App.Subscriptions.UpdateSubscriptionAsync(t.S, new(CurrentPeriodStart: t.Clock.UtcNow, CurrentPeriodEnd: new DateTime(2026, 3, 31, 12, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(2, calls);
        Assert.Equal(20, (await t.App.Credits.GetBalanceAsync(t.C, cu)).Available);
    }
    [Fact]
    public async Task ScheduledVetoRollsBackSubscriptionSave()
    {
        using var t = await Setup();
        var cu = t.C + "credit";
        await t.App.Credits.CreateCurrencyAsync(new(cu, cu));
        await t.App.Credits.SetPlanGrantAsync(t.Plan, cu, new(10, "monthly"));
        var off = t.App.Hooks.OnCreditGrantedBefore((e, ct) => throw new InvalidOperationException("grant veto"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => t.App.Subscriptions.UpdateSubscriptionAsync(t.S, new(Metadata: new() { { "changed", true } })));
        Assert.Empty(await t.App.Credits.ListGrantsAsync(t.C, cu));
        Assert.False((await t.App.Subscriptions.GetSubscriptionAsync(t.S))!.Metadata?.ContainsKey("changed") ?? false);
        off();
        await t.App.Subscriptions.UpdateSubscriptionAsync(t.S, new(Metadata: new() { { "changed", true } }));
        Assert.Single(await t.App.Credits.ListGrantsAsync(t.C, cu));
    }

    [Fact]
    public async Task ConfigRoundTripCountsNoOpsAndRemovesNamedOverride()
    {
        using var t = await Setup("numeric");
        var cu = t.C + "sync-credit";
        var addon = t.P + "sync-addon";
        var config = new ConfigSyncDto("1.0", [new(t.F, t.F, ValueType: "numeric", DefaultValue: "0")], [new(t.P, t.P, Features: [t.F], Addons: [new(addon, addon, FeatureValues: new() { { t.F, "3" } })], Plans: [new(t.Plan, t.Plan, CreditGrants: [new(cu, 10, "once", "none", "retain")])])], CreditCurrencies: [new(cu, cu)], CreditConsumptionRules: [new(t.F, cu, 2)], Subscriptions: [new(t.S, [new(t.F, "8", "timed", new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc))])]);
        var first = await t.App.ConfigSync.SyncFromJsonAsync(config);
        Assert.Empty(first.Errors);
        Assert.True(first.Details!.Created >= 5);
        t.Clock.UtcNow = new(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc);
        var again = await t.App.ConfigSync.SyncFromJsonAsync(config);
        Assert.Empty(again.Errors);
        Assert.Equal(0, again.Details!.Created + again.Details.Updated + again.Details.Removed);
        var exported = await t.App.ConfigSync.ExportConfigAsync([t.S]);
        Assert.Contains(exported.CreditCurrencies!, c => c.Key == cu);
        Assert.Equal("3", exported.Products.Single(p => p.Key == t.P).Addons!.Single(a => a.Key == addon).FeatureValues![t.F]);
        var roundTrip = await t.App.ConfigSync.SyncFromJsonAsync(exported);
        Assert.Empty(roundTrip.Errors);
        Assert.Equal(0, roundTrip.Details!.Created + roundTrip.Details.Updated + roundTrip.Details.Removed);
        var removal = new ConfigSyncDto("1.0", [], [], Subscriptions: [new(t.S, [new(t.F, Remove: true)])]);
        Assert.Equal(1, (await t.App.ConfigSync.SyncFromJsonAsync(removal)).Details!.Removed);
        Assert.Empty((await t.App.ConfigSync.SyncFromJsonAsync(removal)).Errors);
    }
    [Fact]
    public async Task InvalidExpiryRejectsBeforeCatalogWrites()
    {
        using var t = await Setup("numeric");
        var cu = t.C + "bad";
        var config = new ConfigSyncDto("1.0", [], [], CreditCurrencies: [new(cu, cu)], Subscriptions: [new(t.S, [new(t.F, "2", "timed", t.Clock.UtcNow.AddDays(-1))])]);
        await Assert.ThrowsAsync<ValidationException>(() => t.App.ConfigSync.SyncFromJsonAsync(config));
        Assert.Null(await t.App.Credits.GetCurrencyAsync(cu));
    }

    [Fact]
    public async Task AddonAndTimedMeterLimitNeverResetUsage()
    {
        using var t = await Setup();
        var a = t.P + "pack";
        await t.App.Addons.CreateAddonAsync(new(a, t.P, a));
        await t.App.Addons.UpdateAddonAsync(a, new(FeatureValues: new()
        {
            [t.F] = "3"
        }));
        await t.App.Subscriptions.AttachAddonAsync(t.S, a, 2);
        await t.App.Metering.ReportUsageAsync(t.C, t.P, t.F, 12, new("before"));
        await t.App.Subscriptions.AddFeatureOverrideAsync(t.S, t.F, "20", OverrideType.Timed, t.Clock.UtcNow.AddHours(1));
        await t.App.Metering.ReportUsageAsync(t.C, t.P, t.F, 5, new("during"));
        t.Clock.UtcNow = t.Clock.UtcNow.AddHours(1);
        var state = await t.App.Metering.GetUsageAsync(t.C, t.P, t.F);
        Assert.Equal(16, state.Limit);
        Assert.Equal(17, state.Consumed);
        Assert.False(state.HasAccess);
        Assert.Equal(16, (await t.App.FeatureChecker.GetFeatureUsageSummaryAsync(t.C, t.P)).MeteredFeatures[t.F]);
    }
    [Fact]
    public async Task TrialsDelayRecurringGrantAnchor()
    {
        using var t = await Setup();
        var cu = t.C + "trial-credit";
        await t.App.Subscriptions.UpdateSubscriptionAsync(t.S, new(TrialEndDate: new DateTime(2026, 2, 5, 12, 0, 0, DateTimeKind.Utc)));
        await t.App.Credits.CreateCurrencyAsync(new(cu, cu));
        await t.App.Credits.SetPlanGrantAsync(t.Plan, cu, new(10, "monthly"));
        Assert.Equal(0, (await t.App.Credits.GetBalanceAsync(t.C, cu)).Available);
        t.Clock.UtcNow = new(2026, 2, 5, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(10, (await t.App.Credits.GetBalanceAsync(t.C, cu)).Available);
        t.Clock.UtcNow = new(2026, 3, 5, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(20, (await t.App.Credits.GetBalanceAsync(t.C, cu)).Available);
    }
    [Fact]
    public async Task RulesAddedDuringArchiveDoNotBackfill()
    {
        using var t = await Setup();
        var cu = t.C + "archive-credit";
        await t.App.Subscriptions.ArchiveSubscriptionAsync(t.S);
        await t.App.Credits.CreateCurrencyAsync(new(cu, cu));
        await t.App.Credits.SetPlanGrantAsync(t.Plan, cu, new(10, "monthly"));
        t.Clock.UtcNow = new(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        await t.App.Subscriptions.UnarchiveSubscriptionAsync(t.S);
        Assert.Equal(0, (await t.App.Credits.GetBalanceAsync(t.C, cu)).Available);
        t.Clock.UtcNow = new(2026, 4, 30, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(10, (await t.App.Credits.GetBalanceAsync(t.C, cu)).Available);
    }
    [Fact]
    public async Task CatchupBoundRollsBackEveryGrant()
    {
        using var t = await Setup();
        var cu = t.C + "bound";
        await t.App.Credits.CreateCurrencyAsync(new(cu, cu));
        await t.App.Credits.SetPlanGrantAsync(t.Plan, cu, new(1, "monthly"));
        t.Clock.UtcNow = new(2047, 1, 31, 12, 0, 0, DateTimeKind.Utc);
        await Assert.ThrowsAsync<ValidationException>(() => t.App.Credits.ProcessScheduledGrantsAsync(t.C));
        Assert.Empty(await t.App.Credits.ListGrantsAsync(t.C, cu));
        t.Clock.UtcNow = new(2026, 2, 28, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(2, (await t.App.Credits.GetBalanceAsync(t.C, cu)).Available);
    }
}
