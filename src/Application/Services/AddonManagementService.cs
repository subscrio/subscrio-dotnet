using Subscrio.Core.Infrastructure.Repositories;
using Subscrio.Core.Application.Hooks;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Application.Errors;
using Subscrio.Core.Infrastructure.Database;
using static Subscrio.Core.Infrastructure.Database.DatabaseSession;

namespace Subscrio.Core.Application.Services;

public sealed class AddonManagementService
{
    private readonly DatabaseSession _store;
    internal AddonManagementService(DatabaseSession store) => _store = store;
    private static async Task<AddonDto> Dto(DatabaseRow r, DatabaseSession store) => new AddonDto(r.Text("key"), r.Text("product_key"), r.Text("display_name"), r.GetValueOrDefault("description") as string, r.Text("composition_mode"), (int)r.Long("priority"), r.Text("status"), r.GetValueOrDefault("metadata") is null ? null : Read<Dictionary<string, object?>>(r.Text("metadata")), Iso(r.Date("created_at")!.Value), Iso(r.Date("updated_at")!.Value)) { FeatureValues = (await store.Rows($"SELECT f.key,af.value FROM subscrio.addon_features af JOIN subscrio.features f ON f.id=af.feature_id WHERE af.addon_id={r.Long("id")} ORDER BY f.key")).ToDictionary(v => v.Text("key"), v => v.Text("value")) };
    public async Task<AddonDto> CreateAddonAsync(CreateAddonDto input)
    {
        Key(input.Key);
        Validate(input.DisplayName, input.CompositionMode);
        return await _store.Transaction(async st => { var p = await st.Require($"SELECT * FROM subscrio.products WHERE key={input.ProductKey} AND status='active'", "Active product"); if (await st.One($"SELECT id FROM subscrio.addons WHERE key={input.Key}") != null) throw new ConflictException("Addon key already exists"); await st.Insert("addons", new() { ["product_id"] = p.Long("id"), ["key"] = input.Key, ["display_name"] = input.DisplayName, ["description"] = input.Description, ["composition_mode"] = input.CompositionMode, ["priority"] = input.Priority, ["metadata"] = Json(input.Metadata) }); await SaveFeatureValues(st, input.Key, input.FeatureValues?.ToDictionary(x => x.Key, x => (string?)x.Value)); return await Dto(await st.Require($"SELECT a.*,p.key product_key FROM subscrio.addons a JOIN subscrio.products p ON p.id=a.product_id WHERE a.key={input.Key}", "Addon"), st); });
    }
    private static void Validate(string name, string mode)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255)
            throw new ValidationException("Display name is required and must be at most 255 characters");
        if (mode is not ("additive" or "override"))
            throw new ValidationException("Invalid composition mode");
    }
    public async Task<AddonDto?> GetAddonAsync(string key)
    {
        var r = await _store.One($"SELECT a.*,p.key product_key FROM subscrio.addons a JOIN subscrio.products p ON p.id=a.product_id WHERE a.key={key}");
        return r == null ? null : await Dto(r, _store);
    }
    public async Task<List<AddonDto>> ListAddonsAsync(string productKey, int limit = 50, int offset = 0, string? status = null)
    {
        Paging(limit, offset);
        var rows = await _store.Rows($"SELECT a.*,p.key product_key FROM subscrio.addons a JOIN subscrio.products p ON p.id=a.product_id WHERE p.key={productKey} ORDER BY a.key");
        return (await Task.WhenAll(rows.Where(r => status == null || r.Text("status") == status).Skip(offset).Take(limit).Select(r => Dto(r, _store)))).ToList();
    }
    public async Task<AddonDto> UpdateAddonAsync(string key, UpdateAddonDto input)
    {
        return await _store.Transaction(async st => { var a = await st.Require($"SELECT * FROM subscrio.addons WHERE key={key}", "Addon"); Validate(input.DisplayName ?? a.Text("display_name"), input.CompositionMode ?? a.Text("composition_mode")); if (input.CompositionMode == "override" && await st.One($"SELECT id FROM subscrio.subscription_addons WHERE addon_id={a.Long("id")} AND quantity<>1 AND status='active'") != null) throw new ConflictException("Replacement addons require quantity one"); await st.Update("addons", a.Long("id"), new() { ["display_name"] = input.DisplayName ?? a.Text("display_name"), ["description"] = input.Description ?? a.GetValueOrDefault("description"), ["composition_mode"] = input.CompositionMode ?? a.Text("composition_mode"), ["priority"] = input.Priority ?? a.Long("priority"), ["metadata"] = input.Metadata == null ? a.Text("metadata") : Json(input.Metadata), ["updated_at"] = DateTime.UtcNow }); await SaveFeatureValues(st, key, input.FeatureValues); await SaveFeatureValues(st, key, input.FeatureValues); return await Dto(await st.Require($"SELECT a.*,p.key product_key FROM subscrio.addons a JOIN subscrio.products p ON p.id=a.product_id WHERE a.id={a.Long("id")}", "Addon"), st); });
    }
    public async Task ArchiveAddonAsync(string key) => await Status(key, "archived");
    public async Task UnarchiveAddonAsync(string key) => await Status(key, "active");
    private async Task Status(string key, string status)
    {
        var r = await _store.Require($"SELECT id FROM subscrio.addons WHERE key={key}", "Addon");
        await _store.Update("addons", r.Long("id"), new()
        {
            ["status"] = status,
            ["updated_at"] = DateTime.UtcNow
        });
    }
    public async Task DeleteAddonAsync(string key)
    {
        await _store.Transaction(async st => { var a = await st.Require($"SELECT * FROM subscrio.addons WHERE key={key}", "Addon"); if (a.Text("status") != "archived" || await st.One($"SELECT id FROM subscrio.subscription_addons WHERE addon_id={a.Long("id")}") != null) throw new ConflictException("Only archived addons without attachment history can be deleted"); await st.Rows($"DELETE FROM subscrio.addon_features WHERE addon_id={a.Long("id")}"); await st.Rows($"DELETE FROM subscrio.addons WHERE id={a.Long("id")}"); return true; });
    }

    internal static void ValidateValue(string value, string type)
    {
        if (type == "metered")
        {
            if (!long.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n))
                throw new ValidationException("Metered values must be nonnegative safe integers");
            Amount(n);
        }
        else if (type == "numeric" && (!double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n)))
            throw new ValidationException("Invalid numeric value");
        else if (type == "toggle" && !bool.TryParse(value, out _))
            throw new ValidationException("Invalid toggle value");
    }




    private static async Task SaveFeatureValues(DatabaseSession st, string addonKey, Dictionary<string, string?>? values)
    {
        if (values == null)
            return;
        var a = await st.Require($"SELECT id,product_id FROM subscrio.addons WHERE key={addonKey}", "Addon");
        foreach (var (featureKey, value) in values)
        {
            var f = await st.Require($"SELECT f.* FROM subscrio.features f JOIN subscrio.product_features pf ON pf.feature_id=f.id WHERE f.key={featureKey} AND pf.product_id={a.Long("product_id")}", "Associated feature");
            if (value == null)
            {
                await st.Rows($"DELETE FROM subscrio.addon_features WHERE addon_id={a.Long("id")} AND feature_id={f.Long("id")}");
                continue;
            }
            ValidateValue(value, f.Text("value_type"));
            var old = await st.One($"SELECT id FROM subscrio.addon_features WHERE addon_id={a.Long("id")} AND feature_id={f.Long("id")}");
            if (old == null)
                await st.Insert("addon_features", new()
                {
                    ["addon_id"] = a.Long("id"),
                    ["feature_id"] = f.Long("id"),
                    ["value"] = value
                });
            else
                await st.Update("addon_features", old.Long("id"), new()
                {
                    ["value"] = value,
                    ["updated_at"] = DateTime.UtcNow
                });
        }
    }
}
