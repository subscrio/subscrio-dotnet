using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Infrastructure.Database;
using static Subscrio.Core.Infrastructure.Database.DatabaseSession;

namespace Subscrio.Core.Infrastructure.Repositories;

internal sealed class CatalogReader(DatabaseSession store)
{
    internal async Task<List<AddonDto>> AddonsAsync(string? productKey = null, string? featureKey = null)
    {
        var rows = await store.Rows($"SELECT a.*,p.key product_key FROM subscrio.addons a JOIN subscrio.products p ON p.id=a.product_id WHERE (CAST({productKey} AS VARCHAR(255)) IS NULL OR p.key={productKey}) AND (CAST({featureKey} AS VARCHAR(255)) IS NULL OR EXISTS(SELECT 1 FROM subscrio.addon_features af JOIN subscrio.features f ON f.id=af.feature_id WHERE af.addon_id=a.id AND f.key={featureKey})) ORDER BY a.key");
        if (rows.Count == 0)
            return [];
        var values = await store.Rows($"SELECT af.addon_id,f.key,af.value FROM subscrio.addon_features af JOIN subscrio.features f ON f.id=af.feature_id JOIN subscrio.addons a ON a.id=af.addon_id JOIN subscrio.products p ON p.id=a.product_id WHERE (CAST({productKey} AS VARCHAR(255)) IS NULL OR p.key={productKey}) AND (CAST({featureKey} AS VARCHAR(255)) IS NULL OR EXISTS(SELECT 1 FROM subscrio.addon_features selected JOIN subscrio.features sf ON sf.id=selected.feature_id WHERE selected.addon_id=a.id AND sf.key={featureKey}))");
        return rows.Select(r => new AddonDto(r.Text("key"), r.Text("product_key"), r.Text("display_name"),
                r.GetValueOrDefault("description") as string, r.Text("composition_mode"), (int)r.Long("priority"),
                r.Text("status"), r.GetValueOrDefault("metadata") == null ? null : Read<Dictionary<string, object?>>(r.Text("metadata")),
                Iso(r.Date("created_at")!.Value), Iso(r.Date("updated_at")!.Value))
        {
            FeatureValues = values.Where(v => v.Long("addon_id") == r.Long("id")).ToDictionary(v => v.Text("key"), v => v.Text("value"))
        }).ToList();
    }

    internal async Task<List<SubscriptionAddonDto>> SubscriptionAddonsAsync(string subscriptionKey)
    {
        var rows = await store.Rows($"SELECT sa.*,s.key subscription_key,a.key addon_key,p.key product_key FROM subscrio.subscription_addons sa JOIN subscrio.subscriptions s ON s.id=sa.subscription_id JOIN subscrio.addons a ON a.id=sa.addon_id JOIN subscrio.products p ON p.id=a.product_id WHERE s.key={subscriptionKey} ORDER BY a.key");
        if (rows.Count == 0)
            return [];
        var catalog = await AddonsAsync(rows[0].Text("product_key"));
        return rows.Select(r => new SubscriptionAddonDto(r.Text("subscription_key"), r.Text("addon_key"),
            (int)r.Long("quantity"), r.Text("status"), Iso(r.Date("created_at")!.Value), Iso(r.Date("updated_at")!.Value))
        {
            Addon = catalog.Single(a => a.Key == r.Text("addon_key"))
        }).ToList();
    }

    internal async Task<List<ProductFeatureDto>> ProductFeaturesAsync(string productKey) =>
        (await store.Rows($"SELECT f.key,pf.composition_rule,pf.cross_subscription_rule FROM subscrio.product_features pf JOIN subscrio.products p ON p.id=pf.product_id JOIN subscrio.features f ON f.id=pf.feature_id WHERE p.key={productKey} ORDER BY f.key"))
        .Select(r => new ProductFeatureDto(r.Text("key"), new(r.Text("composition_rule"),
            r.Text("cross_subscription_rule") == "legacy" ? null : r.Text("cross_subscription_rule")))).ToList();
}
