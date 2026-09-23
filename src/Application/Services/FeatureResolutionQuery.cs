using System.Globalization;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Application.Errors;
using Subscrio.Core.Infrastructure.Database;
using static Subscrio.Core.Infrastructure.Database.DatabaseSession;
namespace Subscrio.Core.Application.Services;

internal sealed class FeatureResolutionQuery(DatabaseSession store, IClock clock)
{


    internal async Task<FeatureValueExplanationDto> ExplainAsync(string customerKey, string productKey, string featureKey, string? subscriptionKey = null, DateTime? evaluatedAt = null, bool accounting = false)
    {
        var at = evaluatedAt ?? clock.UtcNow;
        var f = await store.Require($"SELECT f.*,p.id product_id,pf.composition_rule,pf.cross_subscription_rule FROM subscrio.features f JOIN subscrio.product_features pf ON pf.feature_id=f.id JOIN subscrio.products p ON p.id=pf.product_id WHERE p.key={productKey} AND f.key={featureKey}", "Associated feature");
        var c = await store.Require($"SELECT id FROM subscrio.customers WHERE key={customerKey}", "Customer");
        var all = (await store.Rows($"SELECT s.*,p.key plan_key,pf.value plan_value FROM subscrio.subscriptions s JOIN subscrio.plans p ON p.id=s.plan_id LEFT JOIN subscrio.plan_features pf ON pf.plan_id=p.id AND pf.feature_id={f.Long("id")} WHERE s.customer_id={c.Long("id")} AND p.product_id={f.Long("product_id")} ORDER BY s.created_at,s.id")).Where(s => subscriptionKey == null || s.Text("key") == subscriptionKey).ToList();
        if (subscriptionKey != null && all.Count == 0)
            throw new ValidationException("Subscription does not belong to this customer and product");
        var legacy = f.Text("cross_subscription_rule") == "legacy";
        if (legacy)
            all.Reverse();
        var subs = new List<SubscriptionFeatureValueDto>();
        var allAddons = await store.Rows($"SELECT sa.subscription_id,a.key,a.composition_mode,a.priority,sa.quantity,af.value FROM subscrio.subscription_addons sa JOIN subscrio.addons a ON a.id=sa.addon_id JOIN subscrio.addon_features af ON af.addon_id=a.id JOIN subscrio.subscriptions s ON s.id=sa.subscription_id JOIN subscrio.plans p ON p.id=s.plan_id WHERE s.customer_id={c.Long("id")} AND p.product_id={f.Long("product_id")} AND sa.status='active' AND af.feature_id={f.Long("id")} ORDER BY a.priority,a.key");
        var allOverrides = await store.Rows($"SELECT o.* FROM subscrio.subscription_feature_overrides o JOIN subscrio.subscriptions s ON s.id=o.subscription_id JOIN subscrio.plans p ON p.id=s.plan_id WHERE s.customer_id={c.Long("id")} AND p.product_id={f.Long("product_id")} AND o.feature_id={f.Long("id")}");
        foreach (var s in all)
        {
            var legacyArchived = s.Bool("is_archived");
            if (legacy && !accounting)
                s["is_archived"] = false;
            if (!(subscriptionKey != null && !accounting) && (!Eligible(s, at) || (legacy && !accounting && s.Date("cancellation_date") != null)))
                continue;
            s["is_archived"] = legacyArchived;
            var value = s.GetValueOrDefault("plan_value") as string ?? f.Text("default_value");
            var sources = new List<FeatureValueSourceDto> { new(s.GetValueOrDefault("plan_value") == null ? "default" : "plan", s.GetValueOrDefault("plan_value") == null ? featureKey : s.Text("plan_key"), value, true) };
            var addons = allAddons.Where(a => a.Long("subscription_id") == s.Long("id")).ToList();
            var replacement = addons.FirstOrDefault(a => a.Text("composition_mode") == "override");
            if (replacement != null)
            {
                sources[0] = sources[0] with
                {
                    Applied = false,
                    Reason = "Replaced by addon"
                };
                value = replacement.Text("value");
            }
            foreach (var a in addons)
            {
                var applied = a.Text("composition_mode") == "additive" || a == replacement;
                sources.Add(new("addon", a.Text("key"), a.Text("value"), applied, (int)a.Long("quantity"), Reason: applied ? null : "Lower replacement priority"));
            }
            value = Combine(new[] { value }.Concat(addons.Where(a => a.Text("composition_mode") == "additive").Select(a => Scale(a.Text("value"), a.Long("quantity"), f.Text("value_type")))).ToList(), f.Text("composition_rule"), f.Text("value_type"));
            var o = allOverrides.FirstOrDefault(o => o.Long("subscription_id") == s.Long("id"));
            if (o != null)
            {
                var active = o.Date("expires_at") == null || o.Date("expires_at") > at;
                if (active)
                {
                    sources = sources.Select(x => x with { Applied = false, Reason = "Replaced by subscription override" }).ToList();
                    value = o.Text("value");
                }
                sources.Add(new("override", s.Text("key"), o.Text("value"), active, ExpiresAt: o.Date("expires_at") is DateTime expiry ? Iso(expiry) : null, Reason: active ? null : "Expired"));
            }
            subs.Add(new(s.Text("key"), value, sources));
        }
        var effective = f.Text("default_value");
        if (legacy)
        {
            foreach (var sub in subs)
            {
                if (sub.Sources.Any(x => x.Kind == "override" && x.Applied))
                {
                    effective = sub.Value;
                    break;
                }
                if (effective == f.Text("default_value") && sub.Sources.Any(x => x.Kind != "default" && x.Applied))
                    effective = sub.Value;
            }
        }
        else if (subs.Count > 0)
            effective = Combine(subs.Select(s => s.Value).ToList(), f.Text("cross_subscription_rule"), f.Text("value_type"));
        return new(Iso(at), effective, new(f.Text("composition_rule"), legacy ? null : f.Text("cross_subscription_rule")), subs);
    }
    private static string Scale(string value, long quantity, string type)
    {
        if (type is not ("numeric" or "metered"))
            return value;
        var n = double.Parse(value, CultureInfo.InvariantCulture) * quantity;
        return Number(n, type);
    }
    private static string Combine(List<string> values, string rule, string type)
    {
        if (rule == "override_wins" || type == "text")
            return values[0];
        if (type == "toggle")
            return values.Any(v => v.Equals("true", StringComparison.OrdinalIgnoreCase)) ? "true" : "false";
        var ns = values.Select(v => double.Parse(v, CultureInfo.InvariantCulture));
        return Number(rule == "additive" ? ns.Sum() : ns.Max(), type);
    }
    private static string Number(double n, string type)
    {
        if (!double.IsFinite(n))
            throw new ValidationException("Composed value overflow");
        if (type == "metered" && (n < 0 || n > 9007199254740991 || n != Math.Truncate(n)))
            throw new ValidationException("Metered value overflow");
        return n.ToString("G", CultureInfo.InvariantCulture);
    }
}
