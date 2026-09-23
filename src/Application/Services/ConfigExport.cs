using System.Text.Json;
using System.Text.Json.Nodes;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Application.Errors;

namespace Subscrio.Core.Application.Services;

internal static class ConfigExport
{
    internal static async Task<List<T>> All<T>(Func<int, int, Task<List<T>>> page)
    {
        var result = new List<T>();
        for (var offset = 0; ; offset += 100)
        {
            var rows = await page(100, offset);
            result.AddRange(rows);
            if (rows.Count < 100)
                return result;
        }
    }
    private static string Canonical(object value)
    {
        static JsonNode? Sort(JsonNode? node) => node switch
        {
            JsonObject obj => new JsonObject(obj.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new KeyValuePair<string, JsonNode?>(p.Key, Sort(p.Value)))),
            JsonArray array => new JsonArray(array.Select(Sort).ToArray()),
            _ => node?.DeepClone()
        };
        return Sort(JsonSerializer.SerializeToNode(value))!.ToJsonString();
    }
    internal static async Task<Dictionary<string, string>> Capture(Subscrio app, ConfigSyncDto config)
    {
        var result = new Dictionary<string, string>();
        void Add(string kind, string key, object? value)
        {
            if (value != null)
                result[kind + ":" + key] = Canonical(value);
        }
        foreach (var c in config.CreditCurrencies ?? [])
        {
            var old = await app.Credits.GetCurrencyAsync(c.Key);
            if (old != null)
                Add("currency", c.Key, new
                {
                    old.DisplayName,
                    old.Status,
                    old.Metadata
                });
        }
        foreach (var f in config.Features)
        {
            if (f.MeteredConfig != null)
                Add("meter", f.Key, (await app.Features.GetFeatureAsync(f.Key))?.MeteredConfig);
            if (f.CreditConsumptionRules != null)
                foreach (var r in await app.Credits.ListConsumptionRulesAsync(f.Key))
                    Add("cost", f.Key + "/" + r.CurrencyKey, r);
        }
        if (config.CreditConsumptionRules != null)
            foreach (var f in await All((limit, offset) => app.Features.ListFeaturesAsync(new(Limit: limit, Offset: offset))))
                foreach (var r in await app.Credits.ListConsumptionRulesAsync(f.Key))
                    Add("cost", f.Key + "/" + r.CurrencyKey, r);
        foreach (var p in config.Products)
        {
            if (await app.Products.GetProductAsync(p.Key) != null)
            {
                var associated = await app.Features.GetFeaturesByProductAsync(p.Key);
                foreach (var f in (p.FeatureResolution ?? []).Keys)
                    if (associated.Any(x => x.Key == f))
                        Add("composition", p.Key + "/" + f, (await app.Products.GetProductAsync(p.Key))!.Features.Single(association => association.FeatureKey == f).Resolution);
            }
            foreach (var a in p.Addons ?? [])
            {
                var old = await app.Addons.GetAddonAsync(a.Key);
                if (old != null)
                    Add("addon", a.Key, new
                    {
                        old.DisplayName,
                        old.Description,
                        old.CompositionMode,
                        old.Priority,
                        old.Status,
                        old.Metadata,
                        FeatureValues = (await app.Addons.GetAddonAsync(a.Key))!.FeatureValues
                    });
            }
            foreach (var plan in p.Plans ?? [])
                if (plan.CreditGrants != null)
                    foreach (var g in await app.Credits.ListPlanGrantsAsync(plan.Key))
                        Add("planGrant", plan.Key + "/" + g.CurrencyKey, g);
        }
        foreach (var s in config.Subscriptions ?? [])
        {
            var old = await app.Subscriptions.GetSubscriptionAsync(s.Key);
            foreach (var o in s.FeatureOverrides)
            {
                var v = old?.FeatureOverrides.FirstOrDefault(f => f.FeatureKey == o.FeatureKey);
                if (v != null)
                    Add("override", s.Key + "/" + o.FeatureKey, new
                    {
                        v.Value,
                        v.Type,
                        v.ExpiresAt
                    });
            }
        }
        return result;
    }
    internal static AccountingSyncReport Compare(Dictionary<string, string> before, Dictionary<string, string> after)
    {
        var changes = new List<AccountingSyncChange>();
        foreach (var key in before.Keys.Union(after.Keys))
        {
            var action = !before.ContainsKey(key) ? "created" : !after.ContainsKey(key) ? "removed" : before[key] == after[key] ? "unchanged" : "updated";
            var i = key.IndexOf(':');
            changes.Add(new(key[..i], key[(i + 1)..], action));
        }
        return new(changes.Count(c => c.Action == "created"), changes.Count(c => c.Action == "updated"), changes.Count(c => c.Action == "removed"), changes.Count(c => c.Action == "unchanged"), changes);
    }
    internal static async Task<ConfigSyncDto> Export(Subscrio app, IEnumerable<string> subscriptionKeys)
    {
        var features = await All((limit, offset) => app.Features.ListFeaturesAsync(new(Limit: limit, Offset: offset)));
        var products = await All((limit, offset) => app.Products.ListProductsAsync(new(Limit: limit, Offset: offset)));
        var plans = await All((limit, offset) => app.Plans.ListPlansAsync(new(Limit: limit, Offset: offset)));
        var cycles = await All((limit, offset) => app.BillingCycles.ListBillingCyclesAsync(new(Limit: limit, Offset: offset)));
        var currencies = await All((limit, offset) => app.Credits.ListCurrenciesAsync(limit, offset));
        var result = new ConfigSyncDto("1.0", features.Select(f => new FeatureConfig(f.Key, f.DisplayName, f.Description, f.ValueType, f.DefaultValue, f.GroupName, f.Validator, f.Metadata, f.Status == "archived", f.MeteredConfig)).ToList(), [], currencies.Select(c => new CreditCurrencyConfig(c.Key, c.DisplayName, c.Status == "archived", c.Metadata)).ToList(), CreditConsumptionRules: []);
        foreach (var f in features)
            foreach (var r in await app.Credits.ListConsumptionRulesAsync(f.Key))
                result.CreditConsumptionRules!.Add(new(f.Key, r.CurrencyKey, r.CreditsPerUnit));
        foreach (var p in products)
        {
            var associated = await app.Features.GetFeaturesByProductAsync(p.Key);
            var addons = await All((limit, offset) => app.Addons.ListAddonsAsync(p.Key, limit, offset));
            var product = new ProductConfig(p.Key, p.DisplayName, p.Description, p.Metadata, p.Status == "archived", associated.Select(f => f.Key).ToList(), [], [], []);
            foreach (var f in associated)
                product.FeatureResolution![f.Key] = (await app.Products.GetProductAsync(p.Key))!.Features.Single(association => association.FeatureKey == f.Key).Resolution;
            foreach (var a in addons)
                product.Addons!.Add(new(a.Key, a.DisplayName, a.Description, a.CompositionMode, a.Priority, a.Status == "archived", a.Metadata, (await app.Addons.GetAddonAsync(a.Key))!.FeatureValues));
            foreach (var plan in plans.Where(x => x.ProductKey == p.Key))
                product.Plans!.Add(new(plan.Key, plan.DisplayName, plan.Description, plan.OnExpireTransitionToBillingCycleKey, (await app.Plans.GetPlanFeaturesAsync(plan.Key)).ToDictionary(f => f.FeatureKey, f => f.Value), cycles.Where(c => c.PlanKey == plan.Key).Select(c => new BillingCycleConfig(c.Key, c.DisplayName, c.Description, c.DurationValue, c.DurationUnit, c.ExternalProductId, c.Status == "archived")).ToList(), plan.Metadata, plan.Status == "archived", await app.Credits.ListPlanGrantsAsync(plan.Key)));
            result.Products.Add(product);
        }
        var subscriptions = new List<SubscriptionOverrideConfig>();
        foreach (var key in subscriptionKeys)
        {
            var s = await app.Subscriptions.GetSubscriptionAsync(key) ?? throw new NotFoundException("Subscription not found: " + key);
            subscriptions.Add(new(key, s.FeatureOverrides.Where(o => o.FeatureKey != null).Select(o => new FeatureOverrideConfig(o.FeatureKey!, o.Value, o.Type, o.ExpiresAt == null ? null : DateTime.Parse(o.ExpiresAt).ToUniversalTime())).ToList()));
        }
        return subscriptions.Count == 0 ? result : result with
        {
            Subscriptions = subscriptions
        };
    }
}

public partial class ConfigSyncService
{
    /// <summary>Export catalog configuration and overrides for explicitly named subscriptions; excludes accounting history.</summary>
    public Task<ConfigSyncDto> ExportConfigAsync(IEnumerable<string>? subscriptionKeys = null) => ConfigExport.Export(Owner ?? throw new InvalidOperationException("Use Subscrio.ConfigSync"), subscriptionKeys ?? []);
}
