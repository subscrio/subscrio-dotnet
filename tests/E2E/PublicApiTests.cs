using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Application.Errors;

namespace Subscrio.Core.Tests.E2E;

public partial class EntitlementTests
{
    [Fact]
    public async Task ConfigSyncCreatesFreshMeteredCatalogAndReplaysExportWithoutChanges()
    {
        using var t = await Setup();
        var key = "sync-" + Guid.NewGuid().ToString("N");
        var config = new ConfigSyncDto("1.0",
            [new(key, key, ValueType: "metered", DefaultValue: "0", MeteredConfig: new("monthly", "hard", "sum", "customer"))],
            [new(key, key, Features: [key], FeatureResolution: new() { [key] = new(AddonRule: "additive") },
                Addons: [new(key, key, FeatureValues: new() { [key] = "5" })],
                Plans: [new(key, key, FeatureValues: new() { [key] = "10" })])]);
        Assert.Empty((await t.App.ConfigSync.SyncFromJsonAsync(config)).Errors);
        Assert.Equal("customer", (await t.App.Features.GetFeatureAsync(key))?.MeteredConfig?.UsageScope);
        Assert.Equal("5", (await t.App.Plans.GetPlanAsync(key))!.Addons.Single().FeatureValues[key]);
        var replay = await t.App.ConfigSync.SyncFromJsonAsync(await t.App.ConfigSync.ExportConfigAsync());
        Assert.Empty(replay.Errors);
        Assert.Equal(0, replay.Details!.Created + replay.Details.Updated + replay.Details.Removed);
    }
    [Fact]
    public async Task AddonDefinitionsOwnAtomicContributionPatchesAndRelatedDtosIncludeValues()
    {
        using var t = await Setup("numeric");
        var second = t.F + "second";
        await t.App.Features.CreateFeatureAsync(new(second, second, "numeric", "0"));
        await t.App.Products.AssociateFeatureAsync(t.P, second);
        var addon = t.P + "addon";
        var created = await t.App.Addons.CreateAddonAsync(new(addon, t.P, "Expansion", FeatureValues: new()
        {
            [t.F] = "10",
            [second] = "100"
        }));
        Assert.Equal("100", created.FeatureValues[second]);
        await t.App.Addons.UpdateAddonAsync(addon, new(DisplayName: "Renamed"));
        Assert.Equal(created.FeatureValues, (await t.App.Addons.GetAddonAsync(addon))!.FeatureValues);
        await t.App.Addons.UpdateAddonAsync(addon, new(FeatureValues: new()
        {
            [t.F] = "15"
        }));
        Assert.Equal("100", (await t.App.Addons.GetAddonAsync(addon))!.FeatureValues[second]);
        await Assert.ThrowsAsync<ValidationException>(() => t.App.Addons.UpdateAddonAsync(addon, new(DisplayName: "Must roll back", FeatureValues: new() { [t.F] = "30", [second] = "invalid" })));
        Assert.Equal("Renamed", (await t.App.Addons.GetAddonAsync(addon))!.DisplayName);
        Assert.Equal("15", (await t.App.Addons.GetAddonAsync(addon))!.FeatureValues[t.F]);
        var attached = await t.App.Subscriptions.AttachAddonAsync(t.S, addon, 2);
        Assert.Equal("15", attached.Addon!.FeatureValues[t.F]);
        Assert.Equal("15", (await t.App.Products.GetProductAsync(t.P))!.Addons.Single().FeatureValues[t.F]);
        Assert.Equal("15", (await t.App.Plans.GetPlanAsync(t.Plan))!.Addons.Single().FeatureValues[t.F]);
        Assert.Equal("15", (await t.App.Features.GetFeatureAsync(t.F))!.Addons.Single().FeatureValues[t.F]);
        Assert.Equal(2, (await t.App.Subscriptions.GetSubscriptionAsync(t.S))!.Addons.Single().Quantity);
        Assert.Equal(addon, (await t.App.Subscriptions.ListSubscriptionsAsync(new(CustomerKey: t.C))).Single().Addons.Single().Addon!.Key);
        Assert.Equal(addon, (await t.App.Plans.GetPlansByProductAsync(t.P)).Single().Addons.Single().Key);
        Assert.Equal(addon, (await t.App.Features.GetFeaturesByProductAsync(t.P)).Single(f => f.Key == t.F).Addons.Single().Key);
        await t.App.Addons.UpdateAddonAsync(addon, new(FeatureValues: new()
        {
            [second] = null
        }));
        Assert.Empty((await t.App.Features.GetFeatureAsync(second))!.Addons);
        Assert.Single((await t.App.Addons.GetAddonAsync(addon))!.FeatureValues);
        Assert.Equal("40", await t.App.FeatureChecker.GetValueForSubscriptionAsync<string>(t.S, t.F));
    }

    [Fact]
    public async Task InvalidAddonCreationRollsBackCatalogAndContributions()
    {
        using var t = await Setup("numeric");
        var addon = t.P + "invalid";
        await Assert.ThrowsAsync<NotFoundException>(() => t.App.Addons.CreateAddonAsync(new(addon, t.P, "Invalid", FeatureValues: new() { [t.F] = "10", ["missing"] = "5" })));
        Assert.Null(await t.App.Addons.GetAddonAsync(addon));
    }

    [Fact]
    public async Task FeatureAssociationsOwnResolutionWithoutPublicCompatibilityModes()
    {
        using var t = await Setup("numeric");
        await t.App.Products.AssociateFeatureAsync(t.P, t.F, new("most_generous", "additive"));
        await t.App.Products.AssociateFeatureAsync(t.P, t.F);
        Assert.Equal(new("most_generous", "additive"), (await t.App.Products.GetProductAsync(t.P))!.Features.Single().Resolution);
        await t.App.Products.AssociateFeatureAsync(t.P, t.F, new());
        Assert.Null((await t.App.Products.GetProductAsync(t.P))!.Features.Single().Resolution.SubscriptionRule);
        var other = t.F + "other";
        await t.App.Features.CreateFeatureAsync(new(other, other, "numeric", "0"));
        await Assert.ThrowsAsync<ValidationException>(() => t.App.Products.AssociateFeatureAsync(t.P, other, new(SubscriptionRule: "legacy")));
        Assert.DoesNotContain((await t.App.Products.GetProductAsync(t.P))!.Features, f => f.FeatureKey == other);
        var explanation = await t.App.FeatureChecker.ExplainForCustomerAsync(t.C, t.P, t.F);
        Assert.Equal(await t.App.FeatureChecker.GetValueForCustomerAsync<string>(t.C, t.P, t.F), explanation.EffectiveValue);
        Assert.Null(explanation.Resolution.SubscriptionRule);
    }

    [Fact]
    public async Task FeatureCrudOwnsMeteringAndGetUsageDoesNotRecordUsage()
    {
        using var t = await Setup();
        Assert.Equal("customer", (await t.App.Features.GetFeatureAsync(t.F))!.MeteredConfig!.UsageScope);
        var updated = await t.App.Features.UpdateFeatureAsync(t.F, new(MeteredConfig: new("monthly", "soft", "sum", "customer")));
        Assert.Equal("soft", updated.MeteredConfig!.Enforcement);
        var report = await t.App.Metering.ReportUsageAsync(t.C, t.P, t.F, 3, new("three"));
        Assert.Equal(3, report.Usage.Consumed);
        Assert.True((await t.App.Metering.GetUsageAsync(t.C, t.P, t.F, new(RequestedUsage: 8))).IsOverage);
        Assert.Single(await t.App.Metering.ListUsageEventsAsync(t.C, t.P, t.F));
        await Assert.ThrowsAsync<ValidationException>(() => t.App.Features.UpdateFeatureAsync(t.F, new(MeteredConfig: new("monthly", "hard", "sum", "subscription"))));
        Assert.Equal("customer", (await t.App.Features.GetFeatureAsync(t.F))!.MeteredConfig!.UsageScope);
        Assert.Null(t.App.Features.GetType().GetMethod("SetMeteredConfigAsync"));
        Assert.Null(t.App.Metering.GetType().GetMethod("GetEntitlementAsync"));
        Assert.Null(t.App.Addons.GetType().GetMethod("SetFeatureValueAsync"));
        Assert.Null(t.App.Addons.GetType().GetMethod("AttachAddonAsync"));
        Assert.Null(t.App.GetType().GetProperty("CreditManagement"));
    }
}
