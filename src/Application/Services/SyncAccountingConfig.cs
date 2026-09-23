using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Domain.ValueObjects;
namespace Subscrio.Core.Application.Services;

internal static class SyncAccountingConfig
{
    internal static async Task Apply(Subscrio app, ConfigSyncDto config, ConfigSyncReport report)
    {
        async Task Run(string key, Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception e) { report.Errors.Add(new("entitlement", key, e.Message)); }
        }
        foreach (var currency in config.CreditCurrencies ?? [])
            await Run(currency.Key, async () => { if (await app.Credits.GetCurrencyAsync(currency.Key) == null) await app.Credits.CreateCurrencyAsync(new(currency.Key, currency.DisplayName, currency.Metadata)); else await app.Credits.UpdateCurrencyAsync(currency.Key, currency.DisplayName, currency.Metadata); if (currency.Archived.HasValue) { if (currency.Archived.Value) await app.Credits.ArchiveCurrencyAsync(currency.Key); else await app.Credits.UnarchiveCurrencyAsync(currency.Key); } });
        if (config.CreditConsumptionRules != null)
        {
            foreach (var featureKey in (await ConfigExport.All((limit, offset) => app.Features.ListFeaturesAsync(new(Limit: limit, Offset: offset)))).Select(f => f.Key).Concat(config.CreditConsumptionRules.Select(r => r.FeatureKey)).Distinct())
                await Run(featureKey, async () => { var desired = config.CreditConsumptionRules.Where(r => r.FeatureKey == featureKey).ToList(); foreach (var old in await app.Credits.ListConsumptionRulesAsync(featureKey)) if (!desired.Any(r => r.CurrencyKey == old.CurrencyKey)) await app.Credits.RemoveConsumptionRuleAsync(featureKey, old.CurrencyKey); foreach (var rule in desired) await app.Credits.SetConsumptionRuleAsync(featureKey, rule.CurrencyKey, rule.CreditsPerUnit); });
        }
        foreach (var f in config.Features)
        {
            if (f.MeteredConfig != null)
                await Run(f.Key, () => app.Features.UpdateFeatureAsync(f.Key, new(MeteredConfig: f.MeteredConfig)));
            if (f.CreditConsumptionRules != null)
                await Run(f.Key, async () => { foreach (var old in await app.Credits.ListConsumptionRulesAsync(f.Key)) if (!f.CreditConsumptionRules.Any(r => r.CurrencyKey == old.CurrencyKey)) await app.Credits.RemoveConsumptionRuleAsync(f.Key, old.CurrencyKey); foreach (var r in f.CreditConsumptionRules) await app.Credits.SetConsumptionRuleAsync(f.Key, r.CurrencyKey, r.CreditsPerUnit); });
        }
        foreach (var p in config.Products)
        {
            foreach (var (f, policy) in p.FeatureResolution ?? [])
                await Run(p.Key, () => app.Products.AssociateFeatureAsync(p.Key, f, policy));
            foreach (var a in p.Addons ?? [])
                await Run(a.Key, async () => { var old = await app.Addons.GetAddonAsync(a.Key); if (old == null) await app.Addons.CreateAddonAsync(new(a.Key, p.Key, a.DisplayName, a.Description, a.CompositionMode ?? "additive", a.Priority ?? 0, a.Metadata)); else { if (old.ProductKey != p.Key) throw new InvalidOperationException("Addon product cannot change"); await app.Addons.UpdateAddonAsync(a.Key, new(a.DisplayName, a.Description, a.CompositionMode, a.Priority, a.Metadata)); } if (a.FeatureValues != null) { foreach (var f in ((await app.Addons.GetAddonAsync(a.Key))!.FeatureValues).Keys) if (!a.FeatureValues.ContainsKey(f)) await app.Addons.UpdateAddonAsync(a.Key, new(FeatureValues: new() { [f] = null })); foreach (var (f, v) in a.FeatureValues) await app.Addons.UpdateAddonAsync(a.Key, new(FeatureValues: new() { [f] = v })); } if (a.Archived.HasValue) { if (a.Archived.Value) await app.Addons.ArchiveAddonAsync(a.Key); else await app.Addons.UnarchiveAddonAsync(a.Key); } });
            foreach (var plan in p.Plans ?? [])
                if (plan.CreditGrants != null)
                    await Run(plan.Key, async () => { foreach (var old in await app.Credits.ListPlanGrantsAsync(plan.Key)) if (!plan.CreditGrants.Any(g => g.CurrencyKey == old.CurrencyKey)) await app.Credits.RemovePlanGrantAsync(plan.Key, old.CurrencyKey); foreach (var g in plan.CreditGrants) await app.Credits.SetPlanGrantAsync(plan.Key, g.CurrencyKey, new(g.Amount, g.Cadence, g.ExpiryPolicy, g.CancellationPolicy)); });
        }
        foreach (var s in config.Subscriptions ?? [])
            await Run(s.Key, async () => { var existing = await app.Subscriptions.GetSubscriptionAsync(s.Key) ?? throw new InvalidOperationException("Config sync only updates existing subscriptions"); foreach (var o in s.FeatureOverrides) { if (o.Remove) { await app.Subscriptions.RemoveFeatureOverrideAsync(s.Key, o.FeatureKey); continue; } var old = existing.FeatureOverrides.FirstOrDefault(f => f.FeatureKey == o.FeatureKey); if (old != null && old.Type == o.Type && old.Value == o.Value && old.ExpiresAt == (o.ExpiresAt.HasValue ? Infrastructure.Database.DatabaseSession.Iso(o.ExpiresAt.Value) : null)) continue; await app.Subscriptions.AddFeatureOverrideAsync(s.Key, o.FeatureKey, o.Value!, Enum.Parse<OverrideType>(o.Type!, true), o.ExpiresAt); } });
    }
}
