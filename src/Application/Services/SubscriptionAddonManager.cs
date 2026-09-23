using Subscrio.Core.Infrastructure.Repositories;
using Subscrio.Core.Application.Hooks;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Application.Errors;
using Subscrio.Core.Infrastructure.Database;
using static Subscrio.Core.Infrastructure.Database.DatabaseSession;

namespace Subscrio.Core.Application.Services;

internal sealed class SubscriptionAddonManager(DatabaseSession _store)
{
    internal TransactionHooks MutationHooks { get; set; } = new(new HookDispatcher());
    internal async Task<SubscriptionAddonDto> AttachAddonAsync(string subscriptionKey, string addonKey, int quantity = 1)
    {
        var input = await MutationHooks.Before("subscription.addonAttached", new AttachAddonInput(subscriptionKey, addonKey, quantity), "quantity");
        quantity = input.Quantity;
        Amount(quantity, "quantity", 1);
        await _store.Transaction(async st => { var s = await st.Require($"SELECT s.*,p.product_id FROM subscrio.subscriptions s JOIN subscrio.plans p ON p.id=s.plan_id WHERE s.key={subscriptionKey}", "Subscription"); await st.LockCustomer((await st.Require($"SELECT key FROM subscrio.customers WHERE id={s.Long("customer_id")}", "Customer")).Text("key")); s = await st.Require($"SELECT s.*,p.product_id FROM subscrio.subscriptions s JOIN subscrio.plans p ON p.id=s.plan_id WHERE s.key={subscriptionKey}", "Subscription"); var a = await st.Require($"SELECT * FROM subscrio.addons WHERE key={addonKey}", "Addon"); if (s.Bool("is_archived") || a.Text("status") != "active" || s.Long("product_id") != a.Long("product_id")) throw new ValidationException("Addon must be active and belong to the subscription product; subscription must not be archived"); if (a.Text("composition_mode") == "override" && quantity != 1) throw new ValidationException("Replacement addons require quantity one"); var old = await st.One($"SELECT id FROM subscrio.subscription_addons WHERE subscription_id={s.Long("id")} AND addon_id={a.Long("id")}"); if (old == null) await st.Insert("subscription_addons", new() { ["subscription_id"] = s.Long("id"), ["addon_id"] = a.Long("id"), ["quantity"] = quantity }); else await st.Update("subscription_addons", old.Long("id"), new() { ["quantity"] = quantity, ["status"] = "active", ["updated_at"] = DateTime.UtcNow }); return true; });
        await MutationHooks.After("subscription.addonAttached", input, new
        {
            subscriptionKey,
            addonKey,
            quantity
        });
        return (await new CatalogReader(_store).SubscriptionAddonsAsync(subscriptionKey)).Single(a => a.AddonKey == addonKey);
    }
    private record AttachAddonInput(string SubscriptionKey, string AddonKey, int Quantity);
    internal async Task DetachAddonAsync(string subscriptionKey, string addonKey)
    {
        var input = new
        {
            subscriptionKey,
            addonKey
        };
        await MutationHooks.Before("subscription.addonDetached", input);
        await _store.Transaction(async st => { var owner = await st.Require($"SELECT c.key FROM subscrio.customers c JOIN subscrio.subscriptions s ON s.customer_id=c.id WHERE s.key={subscriptionKey}", "Subscription customer"); await st.LockCustomer(owner.Text("key")); var attachment = await st.Require($"SELECT sa.id FROM subscrio.subscription_addons sa JOIN subscrio.subscriptions s ON s.id=sa.subscription_id JOIN subscrio.addons a ON a.id=sa.addon_id WHERE s.key={subscriptionKey} AND a.key={addonKey}", "Attachment"); await st.Update("subscription_addons", attachment.Long("id"), new() { ["status"] = "cancelled", ["updated_at"] = DateTime.UtcNow }); return true; });
        await MutationHooks.After("subscription.addonDetached", input, new
        {
            subscriptionKey,
            addonKey
        });
    }
    internal async Task<List<SubscriptionAddonDto>> ListSubscriptionAddonsAsync(string subscriptionKey, int limit = 50, int offset = 0)
    {
        Paging(limit, offset);
        return (await new CatalogReader(_store).SubscriptionAddonsAsync(subscriptionKey)).Skip(offset).Take(limit).ToList();
    }

}
