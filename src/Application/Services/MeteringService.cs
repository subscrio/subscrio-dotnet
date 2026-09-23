using Subscrio.Core.Application.Hooks;
using System.Globalization;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Application.Errors;
using Subscrio.Core.Infrastructure.Database;
using static Subscrio.Core.Infrastructure.Database.DatabaseSession;
namespace Subscrio.Core.Application.Services;

public sealed class MeteringService
{
    internal TransactionHooks MutationHooks { get; set; } = new(new HookDispatcher());
    private readonly DatabaseSession _store; private readonly IClock _clock;
    internal MeteringService(DatabaseSession store, IClock clock)
    {
        _store = store;
        _clock = clock;
    }
    internal static (DateTime Start, DateTime End) CalendarPeriod(DateTime at, string period)
    {
        at = at.ToUniversalTime();
        var start = new DateTime(at.Year, at.Month, at.Day, at.Hour, 0, 0, DateTimeKind.Utc);
        if (period != "hourly")
            start = start.Date;
        if (period == "weekly")
            start = start.AddDays(-((int)start.DayOfWeek + 6) % 7);
        if (period is "monthly" or "yearly")
            start = new DateTime(start.Year, start.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        if (period == "yearly")
            start = new DateTime(start.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return (start, period switch
        {
            "hourly" => start.AddHours(1),
            "daily" => start.AddDays(1),
            "weekly" => start.AddDays(7),
            "monthly" => start.AddMonths(1),
            "yearly" => start.AddYears(1),
            _ => throw new ValidationException("Billing periods require subscription boundaries")
        });
    }
    private record UsageHookInput(string CustomerKey, string ProductKey, string FeatureKey, long Quantity, string IdempotencyKey, string? SubscriptionKey, Dictionary<string, object?>? Metadata);
    private record State(DatabaseRow Row, DatabaseRow? Subscription, DateTime Start, DateTime End, long Limit, long Consumed, DatabaseRow? Balance, bool Active);
    private async Task<State> GetState(DatabaseSession st, string c, string p, string f, UsageOptions options, DateTime at)
    {
        var r = await st.Require($"SELECT m.*,p.id product_id,c.id customer_id FROM subscrio.metered_feature_config m JOIN subscrio.features f ON f.id=m.feature_id JOIN subscrio.product_features pf ON pf.feature_id=f.id JOIN subscrio.products p ON p.id=pf.product_id CROSS JOIN subscrio.customers c WHERE c.key={c} AND p.key={p} AND f.key={f} AND f.value_type='metered'", "Metered feature configuration");
        if ((r.Text("scope") == "subscription") != (options.SubscriptionKey != null))
            throw new ValidationException(r.Text("scope") == "subscription" ? "subscriptionKey is required" : "Customer scoped usage must not specify subscriptionKey");
        var sub = options.SubscriptionKey == null ? null : await st.Require($"SELECT s.* FROM subscrio.subscriptions s JOIN subscrio.plans pl ON pl.id=s.plan_id WHERE s.key={options.SubscriptionKey} AND s.customer_id={r.Long("customer_id")} AND pl.product_id={r.Long("product_id")}", "Subscription");
        DateTime start, end;
        if (r.Text("reset_period") == "billing_period")
        {
            if (sub?.Date("current_period_start") is not DateTime a || sub.Date("current_period_end") is not DateTime b)
                throw new MeteringPeriodException("Billing period bounds are required");
            start = a;
            end = b;
            if (at < start || at >= end)
                throw new MeteringPeriodException("Billing period is stale; update the subscription period");
        }
        else
            (start, end) = CalendarPeriod(at, r.Text("reset_period"));
        var explanation = await new FeatureResolutionQuery(st, _clock).ExplainAsync(c, p, f, options.SubscriptionKey, at, true);
        var limit = Amount(long.Parse(explanation.EffectiveValue, CultureInfo.InvariantCulture), "limit");
        var balance = sub == null ? await st.One($"SELECT * FROM subscrio.usage_balances WHERE customer_id={r.Long("customer_id")} AND product_id={r.Long("product_id")} AND feature_id={r.Long("feature_id")} AND subscription_id IS NULL AND period_start={start}") : await st.One($"SELECT * FROM subscrio.usage_balances WHERE customer_id={r.Long("customer_id")} AND product_id={r.Long("product_id")} AND feature_id={r.Long("feature_id")} AND subscription_id={sub.Long("id")} AND period_start={start}");
        return new(r, sub, start, end, limit, balance?.Long("consumed") ?? 0, balance, explanation.Subscriptions.Count > 0);
    }
    private static UsageDto Result(State s, long requested, string? subscriptionKey)
    {
        var projected = Amount(checked(s.Consumed + requested), "projected usage");
        var over = projected > s.Limit;
        var access = s.Active && (!over || s.Row.Text("enforcement") == "soft");
        return new(access, s.Limit, s.Consumed, Math.Max(0, s.Limit - s.Consumed), requested, projected, over, s.Row.Text("enforcement"), s.Row.Text("scope"), subscriptionKey, Iso(s.Start), Iso(s.End), access ? null : s.Active ? "limit_exceeded" : "no_active_subscription");
    }
    public async Task<UsageDto> GetUsageAsync(string customerKey, string productKey, string featureKey, UsageOptions? options = null)
    {
        options ??= new();
        Amount(options.RequestedUsage, "requestedUsage");
        return Result(await GetState(_store, customerKey, productKey, featureKey, options, _clock.UtcNow), options.RequestedUsage, options.SubscriptionKey);
    }
    public async Task<UsageReportDto> ReportUsageAsync(string customerKey, string productKey, string featureKey, long quantity, UsageReportOptions options)
    {
        Amount(quantity, "quantity", 1);
        RetryKey(options.IdempotencyKey);
        // Explicit nulls are retained in this cross-language request fingerprint.
        var hash = Fingerprint(new Dictionary<string, object?> { ["customerKey"] = customerKey, ["productKey"] = productKey, ["featureKey"] = featureKey, ["quantity"] = quantity, ["subscriptionKey"] = options.SubscriptionKey, ["metadata"] = options.Metadata });
        var replay = false;
        var result = await _store.Transaction(async st =>
        {
            var c = await st.LockCustomer(customerKey);
            var old = await st.One($"SELECT * FROM subscrio.usage_events WHERE customer_id={c.Long("id")} AND idempotency_key={options.IdempotencyKey}");
            if (old != null)
            {
                if (old.Text("request_hash") != hash)
                    throw new IdempotencyConflictException();
                replay = true;
                return Read<UsageReportDto>(old.Text("result_snapshot"));
            }
            var proposed = await MutationHooks.Before("usage.reported", new UsageHookInput(customerKey, productKey, featureKey, quantity, options.IdempotencyKey, options.SubscriptionKey, options.Metadata), "quantity", "metadata");
            quantity = Amount(proposed.Quantity, "quantity", 1);
            options = options with
            {
                Metadata = proposed.Metadata
            };
            var at = _clock.UtcNow;
            var state = await GetState(st, customerKey, productKey, featureKey, new(options.SubscriptionKey), at);
            if (state.Row.Text("aggregation") == "count" && quantity != 1)
                throw new ValidationException("Count aggregation requires quantity one");
            var check = Result(state, quantity, options.SubscriptionKey);
            if (!check.HasAccess)
                throw new UsageLimitExceededException(check);
            var consumed = Amount(checked(state.Consumed + quantity));
            var balance = state.Balance;
            if (balance == null)
                balance = await st.Insert("usage_balances", new()
                {
                    ["customer_id"] = c.Long("id"),
                    ["product_id"] = state.Row.Long("product_id"),
                    ["feature_id"] = state.Row.Long("feature_id"),
                    ["subscription_id"] = state.Subscription?.Long("id"),
                    ["period_start"] = state.Start,
                    ["period_end"] = state.End,
                    ["consumed"] = consumed,
                    ["limit_value"] = state.Limit,
                    ["updated_at"] = at
                });
            else
                await st.Update("usage_balances", balance.Long("id"), new()
                {
                    ["consumed"] = consumed,
                    ["limit_value"] = state.Limit,
                    ["updated_at"] = at
                });
            var e = await st.Insert("usage_events", new()
            {
                ["idempotency_key"] = options.IdempotencyKey,
                ["request_hash"] = hash,
                ["usage_balance_id"] = balance.Long("id"),
                ["customer_id"] = c.Long("id"),
                ["product_id"] = state.Row.Long("product_id"),
                ["feature_id"] = state.Row.Long("feature_id"),
                ["subscription_id"] = state.Subscription?.Long("id"),
                ["quantity"] = quantity,
                ["recorded_at"] = at,
                ["metadata"] = Json(options.Metadata),
                ["result_snapshot"] = "{}"
            });
            var savedResult = new UsageReportDto(e.Text("id"), options.IdempotencyKey, quantity, Iso(at), Result(state with
            {
                Consumed = consumed
            }, 0, options.SubscriptionKey));
            await st.Update("usage_events", e.Long("id"), new()
            {
                ["result_snapshot"] = Json(savedResult)
            });
            return savedResult;
        });
        if (!replay)
            await MutationHooks.After("usage.reported", new UsageHookInput(customerKey, productKey, featureKey, quantity, options.IdempotencyKey, options.SubscriptionKey, options.Metadata), result);
        return result;
    }
    public async Task<List<UsageReportDto>> ListUsageEventsAsync(string customerKey, string productKey, string featureKey, int limit = 50, int offset = 0, string? subscriptionKey = null, DateTime? from = null, DateTime? to = null)
    {
        Paging(limit, offset);
        var rows = await _store.Rows($"SELECT e.*,s.key subscription_key FROM subscrio.usage_events e JOIN subscrio.customers c ON c.id=e.customer_id JOIN subscrio.products p ON p.id=e.product_id JOIN subscrio.features f ON f.id=e.feature_id LEFT JOIN subscrio.subscriptions s ON s.id=e.subscription_id WHERE c.key={customerKey} AND p.key={productKey} AND f.key={featureKey} ORDER BY e.recorded_at DESC,e.id DESC");
        return rows.Where(r => (subscriptionKey == null || r.Text("subscription_key") == subscriptionKey) && (from == null || r.Date("recorded_at") >= from) && (to == null || r.Date("recorded_at") < to)).Skip(offset).Take(limit).Select(r => Read<UsageReportDto>(r.Text("result_snapshot"))).ToList();
    }
}
