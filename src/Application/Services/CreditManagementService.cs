using Subscrio.Core.Application.Hooks;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Application.Errors;
using Subscrio.Core.Infrastructure.Database;
using static Subscrio.Core.Infrastructure.Database.DatabaseSession;
namespace Subscrio.Core.Application.Services;

public sealed class CreditManagementService
{
    internal TransactionHooks MutationHooks { get; set; } = new(new HookDispatcher());
    private readonly DatabaseSession _store; private readonly IClock _clock;
    internal CreditManagementService(DatabaseSession store, IClock clock)
    {
        _store = store;
        _clock = clock;
    }
    public async Task<PlanCreditGrantDto?> GetPlanGrantAsync(string planKey, string currencyKey) => (await ListPlanGrantsAsync(planKey)).FirstOrDefault(g => g.CurrencyKey == currencyKey);
    public async Task<CreditConsumptionRuleDto?> GetConsumptionRuleAsync(string featureKey, string currencyKey) => (await ListConsumptionRulesAsync(featureKey)).FirstOrDefault(r => r.CurrencyKey == currencyKey);
    public async Task<CreditAdjustmentDto> AdjustAsync(CreditAdjustInput input)
    {
        var balance = await AdjustBalanceAsync(input);
        var op = await GetOperationAsync(input.CustomerKey, input.IdempotencyKey);
        return new(op!.Id, input.IdempotencyKey, balance);
    }
    private static Dictionary<string, object?>? Metadata(DatabaseRow r) => r.GetValueOrDefault("metadata") == null ? null : Read<Dictionary<string, object?>>(r.Text("metadata"));
    private static CreditCurrencyDto Currency(DatabaseRow r) => new(r.Text("key"), r.Text("display_name"), r.Text("status"), Metadata(r), Iso(r.Date("created_at")!.Value), Iso(r.Date("updated_at")!.Value));
    public async Task<CreditCurrencyDto> CreateCurrencyAsync(CreateCreditCurrencyDto input)
    {
        Key(input.Key);
        if (string.IsNullOrWhiteSpace(input.DisplayName))
            throw new ValidationException("Display name is required");
        if (await GetCurrencyAsync(input.Key) != null)
            throw new ConflictException("Currency key already exists");
        return Currency(await _store.Insert("credit_currencies", new()
        {
            ["key"] = input.Key,
            ["display_name"] = input.DisplayName,
            ["metadata"] = Json(input.Metadata)
        }));
    }
    public async Task<CreditCurrencyDto?> GetCurrencyAsync(string key)
    {
        var r = await _store.One($"SELECT * FROM subscrio.credit_currencies WHERE key={key}");
        return r == null ? null : Currency(r);
    }
    public async Task<List<CreditCurrencyDto>> ListCurrenciesAsync(int limit = 50, int offset = 0, string? status = null)
    {
        Paging(limit, offset);
        return (await _store.Rows($"SELECT * FROM subscrio.credit_currencies ORDER BY key")).Where(r => status == null || r.Text("status") == status).Skip(offset).Take(limit).Select(Currency).ToList();
    }
    public async Task<CreditCurrencyDto> UpdateCurrencyAsync(string key, string? displayName = null, Dictionary<string, object?>? metadata = null)
    {
        var r = await _store.Require($"SELECT * FROM subscrio.credit_currencies WHERE key={key}", "Currency");
        if (displayName != null && string.IsNullOrWhiteSpace(displayName))
            throw new ValidationException("Display name is required");
        await _store.Update("credit_currencies", r.Long("id"), new()
        {
            ["display_name"] = displayName ?? r.Text("display_name"),
            ["metadata"] = metadata == null ? r.Text("metadata") : Json(metadata),
            ["updated_at"] = _clock.UtcNow
        });
        return (await GetCurrencyAsync(key))!;
    }
    public async Task ArchiveCurrencyAsync(string key) => await CurrencyStatus(key, "archived");
    public async Task UnarchiveCurrencyAsync(string key) => await CurrencyStatus(key, "active");
    private async Task CurrencyStatus(string key, string status)
    {
        var r = await _store.Require($"SELECT id FROM subscrio.credit_currencies WHERE key={key}", "Currency");
        await _store.Update("credit_currencies", r.Long("id"), new()
        {
            ["status"] = status,
            ["updated_at"] = _clock.UtcNow
        });
    }
    public async Task DeleteCurrencyAsync(string key)
    {
        await _store.Transaction(async st => { var c = await st.Require($"SELECT * FROM subscrio.credit_currencies WHERE key={key}", "Currency"); if (c.Text("status") != "archived" || await st.One($"SELECT id FROM subscrio.credit_wallets WHERE credit_currency_id={c.Long("id")} UNION ALL SELECT id FROM subscrio.plan_credit_grants WHERE credit_currency_id={c.Long("id")} UNION ALL SELECT id FROM subscrio.credit_consumption_rules WHERE credit_currency_id={c.Long("id")}") != null) throw new ConflictException("Currency must be archived and have no references"); await st.Rows($"DELETE FROM subscrio.credit_currencies WHERE id={c.Long("id")}"); return true; });
    }
    public async Task SetPlanGrantAsync(string planKey, string currencyKey, PlanCreditGrantInput input)
    {
        Amount(input.Amount, "amount", 1);
        if (input.Cadence is not ("once" or "monthly" or "yearly" or "billing_period") || input.ExpiryPolicy is not ("none" or "grant_period_end") || input.CancellationPolicy is not ("retain" or "expire") || (input.Cadence == "once" && input.ExpiryPolicy == "grant_period_end"))
            throw new ValidationException("Invalid grant policy");
        await _store.Transaction(async st => { var p = await st.Require($"SELECT id FROM subscrio.plans WHERE key={planKey}", "Plan"); var c = await st.Require($"SELECT id FROM subscrio.credit_currencies WHERE key={currencyKey} AND status='active'", "Active currency"); await SettlePlan(st, p.Long("id")); var old = await st.One($"SELECT id FROM subscrio.plan_credit_grants WHERE plan_id={p.Long("id")} AND credit_currency_id={c.Long("id")}"); Dictionary<string, object?> values = new() { ["amount"] = input.Amount, ["cadence"] = input.Cadence, ["expiry_policy"] = input.ExpiryPolicy, ["cancellation_policy"] = input.CancellationPolicy, ["is_active"] = true, ["updated_at"] = _clock.UtcNow }; if (old == null) { values["plan_id"] = p.Long("id"); values["credit_currency_id"] = c.Long("id"); await st.Insert("plan_credit_grants", values); } else await st.Update("plan_credit_grants", old.Long("id"), values); return true; });
    }
    private async Task SettlePlan(DatabaseSession st, long planId)
    {
        var customers = await st.Rows($"SELECT DISTINCT c.id,c.key FROM subscrio.customers c JOIN subscrio.subscriptions s ON s.customer_id=c.id WHERE s.plan_id={planId} ORDER BY c.id");
        foreach (var c in customers)
        {
            await st.LockCustomer(c.Text("key"));
            await Reconcile(st, c.Long("id"), _clock.UtcNow);
        }
    }
    public async Task RemovePlanGrantAsync(string p, string c)
    {
        await _store.Transaction(async st => { var plan = await st.Require($"SELECT id FROM subscrio.plans WHERE key={p}", "Plan"); await SettlePlan(st, plan.Long("id")); await st.Rows($"UPDATE subscrio.plan_credit_grants SET is_active=FALSE,updated_at=NOW() WHERE plan_id={plan.Long("id")} AND credit_currency_id=(SELECT id FROM subscrio.credit_currencies WHERE key={c})"); return true; });
    }
    public async Task<List<PlanCreditGrantDto>> ListPlanGrantsAsync(string p) => (await _store.Rows($"SELECT g.*,c.key currency_key FROM subscrio.plan_credit_grants g JOIN subscrio.plans p ON p.id=g.plan_id JOIN subscrio.credit_currencies c ON c.id=g.credit_currency_id WHERE p.key={p} AND g.is_active=TRUE ORDER BY c.key")).Select(r => new PlanCreditGrantDto(r.Text("currency_key"), r.Long("amount"), r.Text("cadence"), r.Text("expiry_policy"), r.Text("cancellation_policy"))).ToList();
    public async Task SetConsumptionRuleAsync(string f, string c, long creditsPerUnit)
    {
        Amount(creditsPerUnit, "creditsPerUnit", 1);
        await _store.Transaction(async st => { var feature = await st.Require($"SELECT id,value_type FROM subscrio.features WHERE key={f}", "Feature"); if (feature.Text("value_type") == "metered") throw new ValidationException("Metered features cannot have credit consumption rules"); var currency = await st.Require($"SELECT id FROM subscrio.credit_currencies WHERE key={c} AND status='active'", "Active currency"); var old = await st.One($"SELECT id FROM subscrio.credit_consumption_rules WHERE feature_id={feature.Long("id")} AND credit_currency_id={currency.Long("id")}"); if (old == null) await st.Insert("credit_consumption_rules", new() { ["feature_id"] = feature.Long("id"), ["credit_currency_id"] = currency.Long("id"), ["credits_per_unit"] = creditsPerUnit }); else await st.Update("credit_consumption_rules", old.Long("id"), new() { ["credits_per_unit"] = creditsPerUnit, ["updated_at"] = _clock.UtcNow }); return true; });
    }
    public async Task RemoveConsumptionRuleAsync(string f, string c) => await _store.Rows($"DELETE FROM subscrio.credit_consumption_rules WHERE feature_id=(SELECT id FROM subscrio.features WHERE key={f}) AND credit_currency_id=(SELECT id FROM subscrio.credit_currencies WHERE key={c})");
    public async Task<List<CreditConsumptionRuleDto>> ListConsumptionRulesAsync(string f) => (await _store.Rows($"SELECT c.key,r.credits_per_unit FROM subscrio.credit_consumption_rules r JOIN subscrio.features f ON f.id=r.feature_id JOIN subscrio.credit_currencies c ON c.id=r.credit_currency_id WHERE f.key={f} ORDER BY c.key")).Select(r => new CreditConsumptionRuleDto(r.Text("key"), r.Long("credits_per_unit"))).ToList();
    private static async Task<DatabaseRow> Wallet(DatabaseSession st, long customerId, long currencyId) => await st.One($"SELECT * FROM subscrio.credit_wallets WHERE customer_id={customerId} AND credit_currency_id={currencyId}") ?? await st.Insert("credit_wallets", new() { ["customer_id"] = customerId, ["credit_currency_id"] = currencyId });
    private static async Task<DatabaseRow> Operation(DatabaseSession st, long customerId, string key, string type, string hash) => await st.Insert("credit_operations", new() { ["customer_id"] = customerId, ["idempotency_key"] = key, ["operation_type"] = type, ["request_hash"] = hash, ["result_snapshot"] = "{}" });
    private static async Task SaveResult(DatabaseSession st, long id, object result) => await st.Update("credit_operations", id, new() { ["result_snapshot"] = Json(result) });
    private static async Task<DatabaseRow?> Replay(DatabaseSession st, long customerId, string key, string hash)
    {
        var old = await st.One($"SELECT * FROM subscrio.credit_operations WHERE customer_id={customerId} AND idempotency_key={key}");
        if (old != null && old.Text("request_hash") != hash)
            throw new IdempotencyConflictException();
        return old;
    }
    private static async Task Ledger(DatabaseSession st, long operationId, DatabaseRow grant, long amount, string reason, long? featureId = null, object? metadata = null) => await st.Insert("credit_ledger_entries", new() { ["operation_id"] = operationId, ["wallet_id"] = grant.Long("wallet_id"), ["credit_grant_id"] = grant.Long("id"), ["amount"] = amount, ["reason"] = reason, ["feature_id"] = featureId, ["metadata"] = Json(metadata) });
    private static async Task<DatabaseRow> CreateGrant(DatabaseSession st, long customerId, long currencyId, long amount, string type, long operationId, long? subscriptionId = null, long? ruleId = null, string? sourceKey = null, int priority = 0, DateTime? expiresAt = null, DateTime? periodStart = null, DateTime? periodEnd = null, string cancellationPolicy = "retain", object? metadata = null)
    {
        var wallet = await Wallet(st, customerId, currencyId);
        var grant = await st.Insert("credit_grants", new()
        {
            ["wallet_id"] = wallet.Long("id"),
            ["subscription_id"] = subscriptionId,
            ["plan_credit_grant_id"] = ruleId,
            ["source_key"] = sourceKey,
            ["grant_type"] = type,
            ["original_amount"] = amount,
            ["remaining_amount"] = amount,
            ["priority"] = priority,
            ["expires_at"] = expiresAt,
            ["grant_period_start"] = periodStart,
            ["grant_period_end"] = periodEnd,
            ["cancellation_policy"] = cancellationPolicy
        });
        await Ledger(st, operationId, grant, amount, "grant", metadata: metadata);
        return grant;
    }
    private static CreditGrantDto Grant(DatabaseRow r) => new(r.Text("id"), r.Text("currency_key"), r.GetValueOrDefault("subscription_key") as string, r.Text("grant_type"), Amount(r.Long("original_amount")), Amount(r.Long("remaining_amount")), (int)r.Long("priority"), r.Date("expires_at") is DateTime expiry ? Iso(expiry) : null, Iso(r.Date("created_at")!.Value), Iso(r.Date("updated_at")!.Value));
    private static async Task<CreditBalanceDto> Balance(DatabaseSession st, long customerId, string currencyKey, DateTime at)
    {
        var rows = await st.Rows($"SELECT g.*,c.key currency_key,s.key subscription_key FROM subscrio.credit_grants g JOIN subscrio.credit_wallets w ON w.id=g.wallet_id JOIN subscrio.credit_currencies c ON c.id=w.credit_currency_id LEFT JOIN subscrio.subscriptions s ON s.id=g.subscription_id WHERE w.customer_id={customerId} AND c.key={currencyKey} AND g.remaining_amount>0 AND (g.expires_at IS NULL OR g.expires_at>{at}) ORDER BY g.priority,CASE WHEN g.expires_at IS NULL THEN 1 ELSE 0 END,g.expires_at,g.id");
        return new(currencyKey, Amount(rows.Sum(g => g.Long("remaining_amount")), "wallet balance"), rows.Select(Grant).ToList());
    }
    public async Task<CreditGrantDto> GrantAsync(CreditGrantInput input)
    {
        Amount(input.Amount, "amount", 1);
        RetryKey(input.IdempotencyKey);
        if (input.GrantType is not ("manual" or "promotional" or "prepaid"))
            throw new ValidationException("Invalid manual grant type");
        if (input.ExpiresAt?.Kind == DateTimeKind.Unspecified)
            throw new ValidationException("expiresAt must include a timezone");
        var expiry = input.ExpiresAt?.ToUniversalTime();
        var payload = new Dictionary<string, object?> { ["type"] = "grant", ["customerKey"] = input.CustomerKey, ["currencyKey"] = input.CurrencyKey, ["amount"] = input.Amount, ["grantType"] = input.GrantType, ["idempotencyKey"] = input.IdempotencyKey, ["expiresAt"] = expiry is DateTime exp ? Iso(exp) : null };
        payload["priority"] = input.Priority;
        if (input.SubscriptionKey != null)
            payload["subscriptionKey"] = input.SubscriptionKey;
        if (input.Metadata != null)
            payload["metadata"] = input.Metadata;
        var hash = Fingerprint(payload);
        var replay = false;
        var committed = await _store.Transaction(async st => { var c = await st.LockCustomer(input.CustomerKey); var old = await Replay(st, c.Long("id"), input.IdempotencyKey, hash); if (old != null) { replay = true; return Read<CreditGrantDto>(old.Text("result_snapshot")); } input = await MutationHooks.Before("credit.granted", input, "amount", "priority", "expiresAt", "metadata"); Amount(input.Amount, "amount", 1); if (input.ExpiresAt?.Kind == DateTimeKind.Unspecified) throw new ValidationException("expiresAt must include a timezone"); expiry = input.ExpiresAt?.ToUniversalTime(); if (expiry <= _clock.UtcNow) throw new ValidationException("Grant expiry must be in the future"); var currency = await st.Require($"SELECT * FROM subscrio.credit_currencies WHERE key={input.CurrencyKey} AND status='active'", "Active currency"); var sub = input.SubscriptionKey == null ? null : await st.Require($"SELECT id FROM subscrio.subscriptions WHERE key={input.SubscriptionKey} AND customer_id={c.Long("id")}", "Customer subscription"); var op = await Operation(st, c.Long("id"), input.IdempotencyKey, "grant", hash); var g = await CreateGrant(st, c.Long("id"), currency.Long("id"), input.Amount, input.GrantType, op.Long("id"), subscriptionId: sub?.Long("id"), priority: input.Priority, expiresAt: expiry, metadata: input.Metadata); g["currency_key"] = currency.Text("key"); g["subscription_key"] = input.SubscriptionKey; var result = Grant(g); await SaveResult(st, op.Long("id"), result); return result; });
        if (!replay)
            await MutationHooks.After("credit.granted", input, committed);
        return committed;
    }
    public async Task<DuePlanGrantsDto> IssueDuePlanGrantsAsync(IssueDuePlanGrantsInput input) => await _store.Transaction(async st =>
    {
        var s = await st.Require($"SELECT s.*,c.key customer_key FROM subscrio.subscriptions s JOIN subscrio.customers c ON c.id=s.customer_id WHERE s.key={input.SubscriptionKey}", "Subscription");
        await st.LockCustomer(s.Text("customer_key"));
        var before = (await st.Rows($"SELECT id FROM subscrio.credit_grants WHERE subscription_id={s.Long("id")}")).Select(g => g.Long("id")).ToHashSet();
        await Reconcile(st, s.Long("customer_id"), _clock.UtcNow, input.SubscriptionKey);
        var grants = await st.Rows($"SELECT g.*,c.key currency_key,s.key subscription_key FROM subscrio.credit_grants g JOIN subscrio.credit_wallets w ON w.id=g.wallet_id JOIN subscrio.credit_currencies c ON c.id=w.credit_currency_id JOIN subscrio.subscriptions s ON s.id=g.subscription_id WHERE s.id={s.Long("id")} ORDER BY g.id");
        var state = await st.One($"SELECT MIN(next_due_at) next_due_at FROM subscrio.subscription_credit_grant_states WHERE subscription_id={s.Long("id")}");
        return new DuePlanGrantsDto(grants.Where(g => !before.Contains(g.Long("id"))).Select(Grant).ToList(), state?.Date("next_due_at") is DateTime due ? Iso(due) : null, false);
    });
    internal async Task PrepareSubscriptionChange(string customerKey, string subscriptionKey, bool isArchived, DateTime? cancellationDate, DateTime? expirationDate)
    {
        await _store.Transaction(async st =>
        {
            var customer = await st.LockCustomer(customerKey);
            var old = await st.One($"SELECT * FROM subscrio.subscriptions WHERE key={subscriptionKey}");
            if (old == null)
                return true;
            var at = _clock.UtcNow;
            if (old.Bool("is_archived") && !isArchived)
            {
                var rules = await st.Rows($"SELECT * FROM subscrio.plan_credit_grants WHERE plan_id={old.Long("plan_id")} AND is_active=TRUE");
                var originalAnchor = new[] { old.Date("activation_date") ?? old.Date("created_at")!.Value, old.Date("trial_end_date") ?? DateTime.MinValue }.Max();
                foreach (var rule in rules)
                    if (await st.One($"SELECT id FROM subscrio.subscription_credit_grant_states WHERE subscription_id={old.Long("id")} AND credit_currency_id={rule.Long("credit_currency_id")}") == null)
                        await st.Insert("subscription_credit_grant_states", new()
                        {
                            ["subscription_id"] = old.Long("id"),
                            ["credit_currency_id"] = rule.Long("credit_currency_id"),
                            ["anchor_at"] = originalAnchor,
                            ["next_due_at"] = at,
                            ["rule_snapshot"] = Json(rule)
                        });
                foreach (var state in await st.Rows($"SELECT * FROM subscrio.subscription_credit_grant_states WHERE subscription_id={old.Long("id")}"))
                {
                    var snapshot = Read<System.Text.Json.JsonElement>(state.Text("rule_snapshot"));
                    var cadence = snapshot.GetProperty("cadence").GetString();
                    if (cadence == "once")
                        continue;
                    if (cadence == "billing_period")
                    {
                        await st.Update("subscription_credit_grant_states", state.Long("id"), new()
                        {
                            ["next_due_at"] = new[] { at, old.Date("current_period_end") ?? at }.Max()
                        });
                        continue;
                    }
                    var anchor = state.Date("anchor_at")!.Value;
                    var stride = cadence == "yearly" ? 12 : 1;
                    var months = ((at.Year - anchor.Year) * 12 + at.Month - anchor.Month) / stride * stride;
                    var due = anchor.AddMonths(months);
                    if (due <= at)
                        due = anchor.AddMonths(months + stride);
                    await st.Update("subscription_credit_grant_states", state.Long("id"), new()
                    {
                        ["next_due_at"] = due
                    });
                }
            }
            await Reconcile(st, customer.Long("id"), at, subscriptionKey, new[] { cancellationDate ?? DateTime.MaxValue, expirationDate ?? DateTime.MaxValue }.Min());
            return true;
        });
    }
    private record ScheduledGrantInput(string CustomerKey, string CurrencyKey, string SubscriptionKey, long Amount, string GrantType, string IdempotencyKey, int Priority = 0, Dictionary<string, object?>? Metadata = null);
    private async Task<int> Reconcile(DatabaseSession st, long customerId, DateTime at, string? subscriptionKey = null, DateTime? stopBefore = null)
    {
        var issued = 0;
        var subscriptions = await st.Rows($"SELECT * FROM subscrio.subscriptions WHERE customer_id={customerId} ORDER BY id");
        foreach (var sub in subscriptions)
        {
            if ((subscriptionKey != null && sub.Text("key") != subscriptionKey) || sub.Bool("is_archived") || sub.Date("activation_date") > at || sub.Date("trial_end_date") > at)
                continue;
            var stop = new[] { stopBefore ?? DateTime.MaxValue, sub.Date("cancellation_date") ?? DateTime.MaxValue, sub.Date("expiration_date") ?? DateTime.MaxValue }.Min();
            var rules = await st.Rows($"SELECT r.* FROM subscrio.plan_credit_grants r JOIN subscrio.credit_currencies c ON c.id=r.credit_currency_id WHERE r.plan_id={sub.Long("plan_id")} AND r.is_active=TRUE AND c.status='active' ORDER BY r.credit_currency_id");
            foreach (var rule in rules)
            {
                var anchor = sub.Date("activation_date") ?? sub.Date("created_at")!.Value;
                if (sub.Date("trial_end_date") is DateTime trialEnd && trialEnd > anchor)
                    anchor = trialEnd;
                var state = await st.One($"SELECT * FROM subscrio.subscription_credit_grant_states WHERE subscription_id={sub.Long("id")} AND credit_currency_id={rule.Long("credit_currency_id")}") ?? await st.Insert("subscription_credit_grant_states", new()
                {
                    ["subscription_id"] = sub.Long("id"),
                    ["credit_currency_id"] = rule.Long("credit_currency_id"),
                    ["anchor_at"] = anchor,
                    ["next_due_at"] = anchor,
                    ["rule_snapshot"] = Json(rule)
                });
                var cadence = rule.Text("cadence");
                if (cadence == "once" && state.Date("once_issued_at") != null)
                    continue;
                var savedAnchor = state.Date("anchor_at")!.Value;
                DateTime? due = state.Date("next_due_at") ?? at;
                if (cadence == "billing_period")
                {
                    if (sub.Date("current_period_start") == null || sub.Date("current_period_end") == null || sub.Date("current_period_end") <= sub.Date("current_period_start"))
                        throw new ValidationException("Recurring billing period grant requires current subscription boundaries");
                    due = sub.Date("current_period_start");
                    if (state.Date("next_due_at") is DateTime nextDue && nextDue != state.Date("anchor_at") && due < nextDue)
                        continue;
                }
                for (var count = 0; due != null && due <= at && due < stop; count++)
                {
                    if (count >= 240)
                        throw new ValidationException("Credit schedule exceeds 240 catch-up periods; reconcile a smaller time range");
                    DateTime? end = null;
                    if (cadence == "billing_period")
                        end = sub.Date("current_period_end");
                    else if (cadence != "once")
                    {
                        var stride = cadence == "yearly" ? 12 : 1;
                        var months = (due.Value.Year - savedAnchor.Year) * 12 + due.Value.Month - savedAnchor.Month;
                        end = savedAnchor.AddMonths(months + stride);
                        if (end <= due)
                            end = savedAnchor.AddMonths(months + 2 * stride);
                    }
                    var source = $"schedule:{sub.Long("id")}:{rule.Long("credit_currency_id")}:{(cadence == "once" ? "once" : Iso(due.Value))}";
                    if (await st.One($"SELECT id FROM subscrio.credit_grants WHERE source_key={source}") == null)
                    {
                        var customer = await st.Require($"SELECT key FROM subscrio.customers WHERE id={customerId}", "Customer");
                        var currency = await st.Require($"SELECT key FROM subscrio.credit_currencies WHERE id={rule.Long("credit_currency_id")}", "Currency");
                        var input = await MutationHooks.Before("credit.granted", new ScheduledGrantInput(customer.Text("key"), currency.Text("key"), sub.Text("key"), Amount(rule.Long("amount")), "recurring", source), "amount", "priority", "metadata");
                        Amount(input.Amount, "amount", 1);
                        var op = await Operation(st, customerId, source, "scheduled_grant", Fingerprint(new
                        {
                            source
                        }));
                        var g = await CreateGrant(st, customerId, rule.Long("credit_currency_id"), input.Amount, "recurring", op.Long("id"), subscriptionId: sub.Long("id"), ruleId: rule.Long("id"), sourceKey: source, periodStart: due, periodEnd: end, expiresAt: rule.Text("expiry_policy") == "grant_period_end" ? end : null, cancellationPolicy: rule.Text("cancellation_policy"), priority: input.Priority, metadata: input.Metadata);
                        g["currency_key"] = currency.Text("key");
                        g["subscription_key"] = sub.Text("key");
                        await MutationHooks.After("credit.granted", input, Grant(g));
                        await SaveResult(st, op.Long("id"), new
                        {
                            grantId = g.Text("id")
                        });
                        issued++;
                    }
                    Dictionary<string, object?> values = new()
                    {
                        ["next_due_at"] = end,
                        ["rule_snapshot"] = Json(rule),
                        ["updated_at"] = at
                    };
                    if (cadence == "once")
                        values["once_issued_at"] = at;
                    await st.Update("subscription_credit_grant_states", state.Long("id"), values);
                    due = end;
                    if (cadence is "once" or "billing_period")
                        break;
                }
            }
        }
        var ending = await st.Rows($"SELECT g.*,s.cancellation_date,s.expiration_date,s.is_archived FROM subscrio.credit_grants g JOIN subscrio.credit_wallets w ON w.id=g.wallet_id LEFT JOIN subscrio.subscriptions s ON s.id=g.subscription_id WHERE w.customer_id={customerId} AND g.remaining_amount>0 ORDER BY g.id");
        foreach (var g in ending)
        {
            var expired = g.Date("expires_at") <= at;
            var cancelled = g.Text("cancellation_policy") == "expire" && g.GetValueOrDefault("subscription_id") != null && (g.Bool("is_archived") || g.Date("cancellation_date") <= at || g.Date("expiration_date") <= at);
            if (!expired && !cancelled)
                continue;
            var reason = expired ? "expiry" : "cancellation";
            var source = $"{reason}:{g.Long("id")}";
            var op = await Operation(st, customerId, source, reason, Fingerprint(new
            {
                source
            }));
            await Ledger(st, op.Long("id"), g, -Amount(g.Long("remaining_amount")), reason);
            await st.Update("credit_grants", g.Long("id"), new()
            {
                ["remaining_amount"] = 0L,
                ["updated_at"] = at
            });
            await SaveResult(st, op.Long("id"), new
            {
                grantId = g.Text("id"),
                reason
            });
        }
        return issued;
    }
    public async Task<(int Issued, int Customers)> ProcessScheduledGrantsAsync(string? customerKey = null)
    {
        var customers = customerKey == null ? (await _store.Rows($"SELECT key FROM subscrio.customers ORDER BY id")).Select(c => c.Text("key")).ToList() : new List<string> { customerKey };
        var issued = 0;
        foreach (var key in customers)
            issued += await _store.Transaction(async st => { var c = await st.LockCustomer(key); return await Reconcile(st, c.Long("id"), _clock.UtcNow); });
        return (issued, customers.Count);
    }
    public async Task<CreditBalanceDto> GetBalanceAsync(string customerKey, string currencyKey) => await _store.Transaction(async st => { var c = await st.LockCustomer(customerKey); await st.Require($"SELECT id FROM subscrio.credit_currencies WHERE key={currencyKey}", "Currency"); var at = _clock.UtcNow; await Reconcile(st, c.Long("id"), at); return await Balance(st, c.Long("id"), currencyKey, at); });
    public async Task<List<CreditBalanceDto>> ListBalancesAsync(string customerKey) => await _store.Transaction(async st => { var c = await st.LockCustomer(customerKey); var at = _clock.UtcNow; await Reconcile(st, c.Long("id"), at); var currencies = await st.Rows($"SELECT c.key FROM subscrio.credit_wallets w JOIN subscrio.credit_currencies c ON c.id=w.credit_currency_id WHERE w.customer_id={c.Long("id")} ORDER BY c.key"); var results = new List<CreditBalanceDto>(); foreach (var currency in currencies) results.Add(await Balance(st, c.Long("id"), currency.Text("key"), at)); return results; });
    private record Cost(DatabaseRow Rule, long Amount, CreditBalanceDto Balance);
    private static async Task<List<Cost>> Costs(DatabaseSession st, long customerId, string featureKey, long units, DateTime at)
    {
        var rules = await st.Rows($"SELECT r.*,c.key currency_key,c.status currency_status FROM subscrio.credit_consumption_rules r JOIN subscrio.features f ON f.id=r.feature_id JOIN subscrio.credit_currencies c ON c.id=r.credit_currency_id WHERE f.key={featureKey} ORDER BY c.key");
        if (rules.Count == 0)
            throw new ValidationException("Feature has no credit consumption rules");
        var costs = new List<Cost>();
        foreach (var rule in rules)
            costs.Add(new(rule, Amount(checked(Amount(rule.Long("credits_per_unit")) * units), "total credit cost"), await Balance(st, customerId, rule.Text("currency_key"), at)));
        return costs;
    }
    public async Task<CreditCheckDto> CanConsumeAsync(CreditActionDto action)
    {
        Amount(action.Units, "units", 1);
        return await _store.Transaction(async st => { var c = await st.LockCustomer(action.CustomerKey); var at = _clock.UtcNow; await Reconcile(st, c.Long("id"), at); var costs = await Costs(st, c.Long("id"), action.FeatureKey, action.Units, at); var inactive = costs.Any(r => r.Rule.Text("currency_status") != "active"); var enough = costs.All(r => r.Balance.Available >= r.Amount); return new CreditCheckDto(!inactive && enough, costs.Select(r => new CreditCostDto(r.Balance.CurrencyKey, r.Amount, r.Balance.Available)).ToList(), inactive ? "currency_inactive" : !enough ? "insufficient_credits" : null); });
    }
    private static async Task<List<CreditAllocationDto>> Burn(DatabaseSession st, long operationId, CreditBalanceDto balance, long amount, string reason, long? featureId = null, object? metadata = null)
    {
        var remaining = amount;
        var result = new List<CreditAllocationDto>();
        foreach (var dto in balance.Grants)
        {
            if (remaining == 0)
                break;
            var g = await st.Require($"SELECT * FROM subscrio.credit_grants WHERE id={long.Parse(dto.Id)}", "Grant");
            var take = Math.Min(g.Long("remaining_amount"), remaining);
            if (take == 0)
                continue;
            await st.Update("credit_grants", g.Long("id"), new()
            {
                ["remaining_amount"] = g.Long("remaining_amount") - take,
                ["updated_at"] = DateTime.UtcNow
            });
            await Ledger(st, operationId, g, -take, reason, featureId, metadata);
            result.Add(new(balance.CurrencyKey, g.Text("id"), take));
            remaining -= take;
        }
        if (remaining != 0)
            throw new InsufficientCreditsException(new() { new(balance.CurrencyKey, amount, balance.Available) });
        return result;
    }
    public async Task<CreditConsumeDto> ConsumeAsync(CreditConsumeInput input)
    {
        Amount(input.Units, "units", 1);
        RetryKey(input.IdempotencyKey);
        var payload = new Dictionary<string, object?> { ["type"] = "consume", ["customerKey"] = input.CustomerKey, ["featureKey"] = input.FeatureKey, ["units"] = input.Units, ["idempotencyKey"] = input.IdempotencyKey };
        if (input.Metadata != null)
            payload["metadata"] = input.Metadata;
        var hash = Fingerprint(payload);
        var replay = false;
        var committed = await _store.Transaction(async st => { var c = await st.LockCustomer(input.CustomerKey); var old = await Replay(st, c.Long("id"), input.IdempotencyKey, hash); if (old != null) { replay = true; return Read<CreditConsumeDto>(old.Text("result_snapshot")); } input = await MutationHooks.Before("credit.consumed", input, "units", "metadata"); Amount(input.Units, "units", 1); var at = _clock.UtcNow; await Reconcile(st, c.Long("id"), at); var costs = await Costs(st, c.Long("id"), input.FeatureKey, input.Units, at); if (costs.Any(r => r.Rule.Text("currency_status") != "active" || r.Balance.Available < r.Amount)) throw new InsufficientCreditsException(costs.Select(r => new CreditCostDto(r.Balance.CurrencyKey, r.Amount, r.Balance.Available)).ToList()); var op = await Operation(st, c.Long("id"), input.IdempotencyKey, "consume", hash); var result = new CreditConsumeDto(op.Text("id"), input.IdempotencyKey, new(), new()); foreach (var r in costs) { result.Allocations.AddRange(await Burn(st, op.Long("id"), r.Balance, r.Amount, "consumption", r.Rule.Long("feature_id"), input.Metadata)); result.Balances.Add(new(r.Balance.CurrencyKey, r.Balance.Available - r.Amount)); } await SaveResult(st, op.Long("id"), result); return result; });
        if (!replay)
            await MutationHooks.After("credit.consumed", input, committed);
        return committed;
    }
    private async Task<CreditBalanceDto> AdjustBalanceAsync(CreditAdjustInput input)
    {
        if (input.Amount == 0 || input.Amount is < -9007199254740991 or > 9007199254740991)
            throw new ValidationException("Adjustment must be a nonzero safe integer");
        if (string.IsNullOrWhiteSpace(input.Reason))
            throw new ValidationException("Adjustment reason is required");
        RetryKey(input.IdempotencyKey);
        var hash = Fingerprint(new
        {
            type = "adjust",
            input.CustomerKey,
            input.CurrencyKey,
            input.Amount,
            input.Reason,
            input.IdempotencyKey
        });
        var replay = false;
        var committed = await _store.Transaction(async st => { var c = await st.LockCustomer(input.CustomerKey); var old = await Replay(st, c.Long("id"), input.IdempotencyKey, hash); if (old != null) { replay = true; return Read<CreditBalanceDto>(old.Text("result_snapshot")); } input = await MutationHooks.Before("credit.adjusted", input, "amount", "reason"); if (input.Amount == 0 || input.Amount is < -9007199254740991 or > 9007199254740991 || string.IsNullOrWhiteSpace(input.Reason)) throw new ValidationException("Invalid adjustment"); var currency = await st.Require($"SELECT * FROM subscrio.credit_currencies WHERE key={input.CurrencyKey} AND status='active'", "Active currency"); var at = _clock.UtcNow; await Reconcile(st, c.Long("id"), at); var op = await Operation(st, c.Long("id"), input.IdempotencyKey, "adjust", hash); if (input.Amount > 0) await CreateGrant(st, c.Long("id"), currency.Long("id"), input.Amount, "manual", op.Long("id"), metadata: new { reason = input.Reason }); else await Burn(st, op.Long("id"), await Balance(st, c.Long("id"), input.CurrencyKey, at), -input.Amount, "adjustment", metadata: new { reason = input.Reason }); var result = await Balance(st, c.Long("id"), input.CurrencyKey, at); await SaveResult(st, op.Long("id"), result); return result; });
        if (!replay)
            await MutationHooks.After("credit.adjusted", input, committed);
        return committed;
    }
    public async Task<List<CreditGrantDto>> ListGrantsAsync(string customerKey, string currencyKey, int limit = 50, int offset = 0)
    {
        Paging(limit, offset);
        return (await _store.Rows($"SELECT g.*,cu.key currency_key,s.key subscription_key FROM subscrio.credit_grants g JOIN subscrio.credit_wallets w ON w.id=g.wallet_id JOIN subscrio.customers c ON c.id=w.customer_id JOIN subscrio.credit_currencies cu ON cu.id=w.credit_currency_id LEFT JOIN subscrio.subscriptions s ON s.id=g.subscription_id WHERE c.key={customerKey} AND cu.key={currencyKey} ORDER BY g.id DESC")).Skip(offset).Take(limit).Select(Grant).ToList();
    }
    public async Task<CreditOperationDto?> GetOperationAsync(string customerKey, string key)
    {
        var r = await _store.One($"SELECT o.* FROM subscrio.credit_operations o JOIN subscrio.customers c ON c.id=o.customer_id WHERE c.key={customerKey} AND o.idempotency_key={key}");
        return r == null ? null : new(r.Text("id"), r.Text("operation_type"), Read<System.Text.Json.JsonElement>(r.Text("result_snapshot")), Iso(r.Date("created_at")!.Value));
    }
    public async Task<List<CreditLedgerEntryDto>> ListLedgerEntriesAsync(string customerKey, string currencyKey, int limit = 50, int offset = 0)
    {
        Paging(limit, offset);
        return (await _store.Rows($"SELECT l.* FROM subscrio.credit_ledger_entries l JOIN subscrio.credit_wallets w ON w.id=l.wallet_id JOIN subscrio.customers c ON c.id=w.customer_id JOIN subscrio.credit_currencies cu ON cu.id=w.credit_currency_id WHERE c.key={customerKey} AND cu.key={currencyKey} ORDER BY l.created_at DESC,l.id DESC")).Skip(offset).Take(limit).Select(r => new CreditLedgerEntryDto(r.Text("id"), r.Text("operation_id"), r.Text("credit_grant_id"), r.Long("amount"), r.Text("reason"), Iso(r.Date("created_at")!.Value), Metadata(r))).ToList();
    }
}
