using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Application.Errors;
using Subscrio.Core.Infrastructure.Database;
namespace Subscrio.Core.Infrastructure.Repositories;

internal sealed class MeteredConfigRepository(DatabaseSession _store)
{
    internal static void ValidateConfig(MeteredFeatureConfigDto c)
    {
        if (!new[] { "hourly", "daily", "weekly", "monthly", "yearly", "billing_period" }.Contains(c.ResetPeriod) || c.Enforcement is not ("hard" or "soft") || c.Aggregation is not ("count" or "sum") || c.UsageScope is not ("customer" or "subscription") || (c.ResetPeriod == "billing_period" && c.UsageScope != "subscription"))
            throw new ValidationException("Invalid metered configuration");
    }
    public async Task SetMeteredConfigAsync(string featureKey, MeteredFeatureConfigDto config)
    {
        ValidateConfig(config);
        await _store.Transaction(async st => { var f = await st.Require($"SELECT * FROM subscrio.features WHERE key={featureKey}", "Feature"); if (f.Text("value_type") != "metered") throw new ValidationException("Meter configuration requires metered feature type"); var old = await st.One($"SELECT * FROM subscrio.metered_feature_config WHERE feature_id={f.Long("id")}"); if (old != null && (old.Text("scope") != config.UsageScope || old.Text("reset_period") != config.ResetPeriod || old.Text("aggregation") != config.Aggregation) && await st.One($"SELECT id FROM subscrio.usage_events WHERE feature_id={f.Long("id")}") != null) throw new ValidationException("Scope, reset period and aggregation cannot change after usage has been recorded"); Dictionary<string, object?> values = new() { ["reset_period"] = config.ResetPeriod, ["enforcement"] = config.Enforcement, ["aggregation"] = config.Aggregation, ["scope"] = config.UsageScope, ["updated_at"] = DateTime.UtcNow }; if (old != null) await st.Update("metered_feature_config", old.Long("id"), values); else { values["feature_id"] = f.Long("id"); await st.Insert("metered_feature_config", values); } return true; });
    }
    public async Task<MeteredFeatureConfigDto?> GetMeteredConfigAsync(string featureKey)
    {
        var r = await _store.One($"SELECT m.* FROM subscrio.metered_feature_config m JOIN subscrio.features f ON f.id=m.feature_id WHERE f.key={featureKey}");
        return r == null ? null : new(r.Text("reset_period"), r.Text("enforcement"), r.Text("aggregation"), r.Text("scope"));
    }

}
