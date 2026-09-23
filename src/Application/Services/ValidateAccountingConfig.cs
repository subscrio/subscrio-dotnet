using Subscrio.Core.Infrastructure.Repositories;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Application.Errors;
using Subscrio.Core.Application.Utils;
using Subscrio.Core.Domain.ValueObjects;
namespace Subscrio.Core.Application.Services;

internal static class ValidateAccountingConfig
{
    internal static async Task Validate(Subscrio app, ConfigSyncDto config)
    {
        static void Unique(IEnumerable<string> keys, string label)
        {
            var values = keys.ToList();
            if (values.Distinct().Count() != values.Count)
                throw new ValidationException("Duplicate " + label);
        }
        async Task Currency(string key)
        {
            if (!(config.CreditCurrencies ?? []).Any(c => c.Key == key) && await app.Credits.GetCurrencyAsync(key) == null)
                throw new ValidationException("Unknown currency: " + key);
        }
        async Task<string> FeatureType(string key) => config.Features.FirstOrDefault(f => f.Key == key)?.ValueType ?? (await app.Features.GetFeatureAsync(key))?.ValueType ?? throw new ValidationException("Unknown feature: " + key);
        Unique((config.CreditCurrencies ?? []).Select(c => c.Key), "currency key");
        Unique(config.Products.SelectMany(p => p.Addons ?? []).Select(a => a.Key), "add-on key");
        Unique((config.Subscriptions ?? []).Select(s => s.Key), "subscription key");
        var costs = (config.CreditConsumptionRules ?? []).Concat(config.Features.SelectMany(f => (f.CreditConsumptionRules ?? []).Select(r => new CreditConsumptionConfig(f.Key, r.CurrencyKey, r.CreditsPerUnit)))).ToList();
        Unique(costs.Select(c => c.FeatureKey + "\0" + c.CurrencyKey), "feature/currency cost");
        foreach (var rule in costs)
        {
            await Currency(rule.CurrencyKey);
            Infrastructure.Database.DatabaseSession.Amount(rule.CreditsPerUnit, "creditsPerUnit", 1);
            if (await FeatureType(rule.FeatureKey) == "metered")
                throw new ValidationException("Metered features cannot have credit consumption rules");
        }
        foreach (var f in config.Features)
        {
            if (f.ValueType == "metered")
            {
                if (f.MeteredConfig == null)
                    throw new ValidationException("Metered config required: " + f.Key);
                MeteredConfigRepository.ValidateConfig(f.MeteredConfig);
            }
            else if (f.MeteredConfig != null)
                throw new ValidationException("Only metered features accept metered config");
        }
        foreach (var p in config.Products)
        {
            var associated = p.Features ?? (await app.Products.GetProductAsync(p.Key) == null ? new List<string>() : (await app.Features.GetFeaturesByProductAsync(p.Key)).Select(f => f.Key).ToList());
            foreach (var (key, policy) in p.FeatureResolution ?? [])
            {
                if (!associated.Contains(key))
                    throw new ValidationException("Composition feature must be associated with product");
                if (policy.AddonRule is not (null or "additive" or "most_generous" or "override_wins") || policy.SubscriptionRule is not (null or "additive" or "most_generous" or "override_wins"))
                    throw new ValidationException("Invalid composition policy");
            }
            foreach (var addon in p.Addons ?? [])
            {
                var old = await app.Addons.GetAddonAsync(addon.Key);
                if (old != null && old.ProductKey != p.Key)
                    throw new ValidationException("Addon product cannot change");
                foreach (var (key, value) in addon.FeatureValues ?? [])
                {
                    if (!associated.Contains(key))
                        throw new ValidationException("Add-on feature must belong to product");
                    AddonManagementService.ValidateValue(value, await FeatureType(key));
                }
            }
            foreach (var plan in p.Plans ?? [])
            {
                Unique((plan.CreditGrants ?? []).Select(g => g.CurrencyKey), "plan/currency grant");
                foreach (var grant in plan.CreditGrants ?? [])
                {
                    await Currency(grant.CurrencyKey);
                    Infrastructure.Database.DatabaseSession.Amount(grant.Amount, "amount", 1);
                    if (grant.Cadence is not ("once" or "monthly" or "yearly" or "billing_period") || grant.ExpiryPolicy is not ("none" or "grant_period_end") || grant.CancellationPolicy is not ("retain" or "expire") || grant.Cadence == "once" && grant.ExpiryPolicy == "grant_period_end")
                        throw new ValidationException("Invalid grant policy");
                }
            }
        }
        foreach (var sub in config.Subscriptions ?? [])
        {
            var existing = await app.Subscriptions.GetSubscriptionAsync(sub.Key) ?? throw new ValidationException("Config sync only updates existing subscriptions");
            Unique(sub.FeatureOverrides.Select(o => o.FeatureKey), "subscription override");
            var associated = (await app.Features.GetFeaturesByProductAsync(existing.ProductKey)).Select(f => f.Key).ToHashSet();
            foreach (var o in sub.FeatureOverrides)
            {
                if (!associated.Contains(o.FeatureKey))
                    throw new ValidationException("Override feature must belong to subscription product");
                if (o.Remove)
                {
                    if (o.Value != null || o.Type != null || o.ExpiresAt != null)
                        throw new ValidationException("Removal cannot include value, type or expiration");
                    continue;
                }
                if (o.Value == null || o.Type == null)
                    throw new ValidationException("Override value and type required");
                var old = existing.FeatureOverrides.FirstOrDefault(f => f.FeatureKey == o.FeatureKey);
                var same = old?.Type == o.Type && old.Value == o.Value && old.ExpiresAt == (o.ExpiresAt.HasValue ? Infrastructure.Database.DatabaseSession.Iso(o.ExpiresAt.Value) : null);
                if (!same && o.ExpiresAt <= app.Subscriptions.Clock.UtcNow)
                    throw new ValidationException("Changed timed override requires future expiration");
                AddonManagementService.ValidateValue(o.Value, await FeatureType(o.FeatureKey));
                if (o.Type is not ("timed" or "temporary" or "permanent") || o.Type == "timed" && o.ExpiresAt == null || o.Type != "timed" && o.ExpiresAt != null || o.ExpiresAt?.Kind == DateTimeKind.Unspecified)
                    throw new ValidationException("Invalid override expiration/type");
            }
        }
    }
}
