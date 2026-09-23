using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Application.Errors;
using Subscrio.Core.Config;
using Subscrio.Core.Domain.ValueObjects;
using SubscrioInstance = Subscrio.Core.Subscrio;
namespace Subscrio.Sample;

internal static class CatalogAccountingDemo
{
    private sealed class DemoClock : IClock
    {
        public DateTime UtcNow { get; set; } = Utc(2026, 1, 31, 12);
    }
    private static DateTime Utc(int year, int month, int day, int hour = 0) => new(year, month, day, hour, 0, 0, DateTimeKind.Utc);
    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("Demo assertion failed: " + message);
    }
    private static void Pass(string message) => Console.WriteLine("CAPABILITIES PASS: " + message);
    private static async Task Reject<T>(Func<Task> action) where T : Exception
    {
        try
        {
            await action();
        }
        catch (T) { return; }
        throw new InvalidOperationException("Expected rejection: " + typeof(T).Name);
    }

    internal static async Task Run(SubscrioConfig config)
    {
        var clock = new DemoClock();
        using var app = new SubscrioInstance(new()
        {
            Database = config.Database,
            AdminPassphrase = config.AdminPassphrase,
            Clock = clock
        });
        var prefix = "demo-" + Guid.NewGuid().ToString("N")[..8];
        var p = prefix + "-studio";
        var seats = prefix + "-seats";
        var meter = prefix + "-requests";
        var action = prefix + "-render";
        var plan = prefix + "-pro";
        var cycle = prefix + "-monthly";
        var c = prefix + "-customer";
        var s = prefix + "-subscription";
        var addon = prefix + "-seat-pack";
        var currency = prefix + "-credits";
        Console.WriteLine("\nAdd-ons, period quotas, shared credits, and timed overrides");
        await app.Products.CreateProductAsync(new(p, "Creative studio"));
        foreach (var (key, type, value) in new[] { (seats, "numeric", "0"), (meter, "metered", "0"), (action, "toggle", "true") })
        {
            await app.Features.CreateFeatureAsync(new(key, key, type, value, MeteredConfig: type == "metered" ? new("monthly", "hard", "sum", "customer") : null));
            await app.Products.AssociateFeatureAsync(p, key, new(SubscriptionRule: "most_generous"));
        }
        await app.Addons.CreateAddonAsync(new(addon, p, "Three extra seats", FeatureValues: new()
        {
            [seats] = "3"
        }));
        await app.Plans.CreatePlanAsync(new(p, plan, "Pro"));
        await app.Plans.SetFeatureValueAsync(plan, seats, "5");
        await app.Plans.SetFeatureValueAsync(plan, meter, "10");
        await app.BillingCycles.CreateBillingCycleAsync(new(plan, cycle, "Monthly", "months", DurationValue: 1));
        await app.Customers.CreateCustomerAsync(new(c));
        await app.Subscriptions.CreateSubscriptionAsync(new(s, c, cycle, ActivationDate: clock.UtcNow, CurrentPeriodStart: clock.UtcNow, CurrentPeriodEnd: Utc(2026, 2, 28, 12)));
        await app.Subscriptions.AttachAddonAsync(s, addon, 2);
        Check(await app.FeatureChecker.GetValueForSubscriptionAsync<string>(s, seats) == "11", "additive quantity");
        Check((await app.Features.GetFeatureAsync(seats))!.Addons.Single().FeatureValues[seats] == "3", "feature add-on DTO");
        Check((await app.Plans.GetPlanAsync(plan))!.Addons.Single().Key == addon, "plan add-on DTO");
        Check((await app.Subscriptions.GetSubscriptionAsync(s))!.Addons.Single().Quantity == 2, "subscription add-on DTO");
        Pass("Nested feature, plan and subscription DTOs include add-ons");
        Pass("Add-on quantity 2: 5 base seats + 2 × 3 = 11");
        await app.Subscriptions.CreateSubscriptionAsync(new(s + "-second", c, cycle, ActivationDate: clock.UtcNow));
        Check(await app.FeatureChecker.GetValueForCustomerAsync<string>(c, p, seats) == "11", "cross-subscription max");
        await app.Products.AssociateFeatureAsync(p, seats, new("additive", "additive"));
        Check(await app.FeatureChecker.GetValueForCustomerAsync<string>(c, p, seats) == "16", "cross-subscription additive");
        Pass("Across subscriptions: most-generous 11; explicit additive 16");
        await app.Subscriptions.AddFeatureOverrideAsync(s, seats, "20", OverrideType.Timed, Utc(2026, 2, 1));
        Check(await app.FeatureChecker.GetValueForSubscriptionAsync<string>(s, seats) == "20", "timed value");
        Check((await app.FeatureChecker.ExplainForSubscriptionAsync(s, seats)).Subscriptions[0].Sources.Any(source => source.Kind == "override" && source.Applied), "override explanation");
        clock.UtcNow = Utc(2026, 2, 1);
        Check(await app.FeatureChecker.GetValueForSubscriptionAsync<string>(s, seats) == "11", "expiry boundary");
        Check(!(await app.Subscriptions.GetSubscriptionAsync(s))!.FeatureOverrides[0].IsActive, "expired override status");
        Pass("Timed override: 20 before expiry; 11 exactly at expiry, retained in administration");

        var request = new UsageReportOptions(prefix + "-usage");
        var usage = await app.Metering.ReportUsageAsync(c, p, meter, 7, request);
        Check(usage.Usage.Remaining == 3, "remaining quota");
        Check(await app.Metering.ReportUsageAsync(c, p, meter, 7, request) == usage, "usage replay");
        await Reject<UsageLimitExceededException>(() => app.Metering.ReportUsageAsync(c, p, meter, 4, new(prefix + "-hard")));
        Pass("Hard quota: 7 used, 3 remaining; replay unchanged; 4 more rejected");
        await app.Features.UpdateFeatureAsync(meter, new(MeteredConfig: new("monthly", "soft", "sum", "customer")));
        Check((await app.Metering.ReportUsageAsync(c, p, meter, 4, new(prefix + "-soft"))).Usage.IsOverage, "soft overage");
        clock.UtcNow = Utc(2026, 3, 1);
        Check((await app.Metering.GetUsageAsync(c, p, meter)).Consumed == 0, "monthly reset");
        Check(await app.Metering.ReportUsageAsync(c, p, meter, 7, request) == usage, "replay across reset");
        Pass("Soft quota records 11 used; March resets to 0; February replay keeps its original result");

        await app.Credits.CreateCurrencyAsync(new(currency, "Render credits"));
        await app.Credits.SetConsumptionRuleAsync(action, currency, 3);
        var promo = await app.Credits.GrantAsync(new(c, currency, 10, "promotional", prefix + "-promo", ExpiresAt: Utc(2026, 3, 2)));
        await app.Credits.GrantAsync(new(c, currency, 20, "prepaid", prefix + "-paid", Priority: 1));
        var spend = new CreditConsumeInput(c, action, 4, prefix + "-spend");
        var burn = await app.Credits.ConsumeAsync(spend);
        Check(burn.Allocations[0].GrantId == promo.Id && burn.Allocations[0].Amount == 10 && burn.Balances[0].Available == 18, "burn order");
        Check((await app.Credits.ConsumeAsync(spend)).OperationId == burn.OperationId, "credit replay");
        await Reject<InsufficientCreditsException>(() => app.Credits.ConsumeAsync(new(c, action, 7, prefix + "-too-much")));
        Pass("Credits: 4 renders × 3 = 12; promotion burns before prepaid; 18 remain; retries do not spend twice");
        await app.Credits.GrantAsync(new(c, currency, 5, "promotional", prefix + "-expiry", ExpiresAt: Utc(2026, 3, 2)));
        clock.UtcNow = Utc(2026, 3, 2);
        Check((await app.Credits.GetBalanceAsync(c, currency)).Available == 18, "credit expiry");
        Pass("Credit expiry removes the unused promotion and posts a ledger debit");
        await app.Credits.SetPlanGrantAsync(plan, currency, new(10, "monthly", CancellationPolicy: "expire"));
        var scheduled = await app.Credits.IssueDuePlanGrantsAsync(new(s));
        Check(scheduled.Issued.Count == 2 && scheduled.NextDueAt == "2026-03-31T12:00:00.000Z", "anchored monthly schedule");
        await app.Subscriptions.UpdateSubscriptionAsync(s, new(CancellationDate: clock.UtcNow));
        await app.Subscriptions.UpdateSubscriptionAsync(s + "-second", new(CancellationDate: clock.UtcNow));
        Check((await app.Credits.GetBalanceAsync(c, currency)).Available == 18, "prepaid survives cancellation");
        var ledger = await app.Credits.ListLedgerEntriesAsync(c, currency, 500);
        Check(ledger.Sum(row => row.Amount) == 18 && ledger.Any(row => row.Reason == "cancellation"), "ledger reconciliation");
        Pass("Monthly grants anchor Jan 31 → Feb 28 → Mar 31; cancellation expires only subscription grants; prepaid 18 remains");
        Check((await app.Metering.ListUsageEventsAsync(c, p, meter)).Count == 2, "only accepted usage events");
        Pass("Ledger total equals wallet balance; rejected operations created no usage events");
        Console.WriteLine("CAPABILITIES DEMO VERIFIED");
    }
}
