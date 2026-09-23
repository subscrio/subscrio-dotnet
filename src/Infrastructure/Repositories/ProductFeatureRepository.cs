using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Application.Errors;
using Subscrio.Core.Infrastructure.Database;
namespace Subscrio.Core.Infrastructure.Repositories;

internal sealed class ProductFeatureRepository(DatabaseSession store)
{
    internal async Task AssociateAsync(string productKey, string featureKey, FeatureResolutionOptions? options)
    {
        await store.Transaction(async st =>
        {
            var p = await st.Require($"SELECT id FROM subscrio.products WHERE key={productKey}", "Product");
            var f = await st.Require($"SELECT id,value_type FROM subscrio.features WHERE key={featureKey}", "Feature");
            var old = await st.One($"SELECT * FROM subscrio.product_features WHERE product_id={p.Long("id")} AND feature_id={f.Long("id")}");
            var addonRule = options?.AddonRule ?? old?.Text("composition_rule") ?? (f.Text("value_type") == "text" ? "override_wins" : f.Text("value_type") == "toggle" ? "most_generous" : "additive");
            var subscriptionRule = options == null ? old?.Text("cross_subscription_rule") ?? "legacy" : options.SubscriptionRule ?? "legacy";
            if (addonRule is not ("additive" or "most_generous" or "override_wins") || options?.SubscriptionRule is not (null or "additive" or "most_generous" or "override_wins"))
                throw new ValidationException("Invalid feature resolution rule");
            if (f.Text("value_type") == "text" && (addonRule != "override_wins" || subscriptionRule is not ("legacy" or "override_wins")))
                throw new ValidationException("Text features require override_wins");
            Dictionary<string, object?> values = new()
            {
                ["composition_rule"] = addonRule,
                ["cross_subscription_rule"] = subscriptionRule
            };
            if (old == null)
            {
                values["product_id"] = p.Long("id");
                values["feature_id"] = f.Long("id");
                await st.Insert("product_features", values);
            }
            else
                await st.Update("product_features", old.Long("id"), values);
            return true;
        });
    }
}
