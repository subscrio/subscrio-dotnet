using Microsoft.EntityFrameworkCore.Storage;
using Subscrio.Core.Application.Services;
using Subscrio.Core.Application.Errors;
using Subscrio.Core.Config;
using Subscrio.Core.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Subscrio.Core.Application.Repositories;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Infrastructure.Database;

namespace Subscrio.Core.Infrastructure.Repositories;

public class EfFeatureRepository : IFeatureRepository
{
    private readonly SubscrioDbContext _db;

    public EfFeatureRepository(SubscrioDbContext db, DatabaseConfig? config = null)
    {
        _db = db;
        Config = config ?? new()
        {
            ConnectionString = db.Database.GetDbConnection().ConnectionString,
            DatabaseType = db.Database.IsSqlServer() ? DatabaseType.SqlServer : DatabaseType.PostgreSQL
        };
    }

    private DatabaseConfig Config
    {
        get;
    }
    private async Task<FeatureRecord?> Hydrate(FeatureRecord? record)
    {
        if (record?.ValueType == "metered")
            record.MeteredConfig = await new MeteredConfigRepository(new DatabaseSession(Config)).GetMeteredConfigAsync(record.Key);
        return record;
    }
    private async Task<List<FeatureRecord>> Hydrate(List<FeatureRecord> records)
    {
        foreach (var r in records)
            await Hydrate(r);
        return records;
    }
    public async Task<FeatureRecord> SaveAsync(FeatureRecord record)
    {
        if (record.ValueType == "metered" && record.MeteredConfig == null)
            throw new ValidationException("Metered features require meteredConfig");
        if (record.ValueType != "metered" && record.MeteredConfig != null)
            throw new ValidationException("Only metered features accept meteredConfig");
        if (record.MeteredConfig != null)
            MeteredConfigRepository.ValidateConfig(record.MeteredConfig);
        return await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            var store = new DatabaseSession(Config, _db.Database.GetDbConnection(), tx.GetDbTransaction());
            if (record.Id != 0)
            {
                var old = await store.One($"SELECT value_type FROM subscrio.features WHERE id={record.Id}");
                if (old?.Text("value_type") != record.ValueType && await store.One($"SELECT id FROM subscrio.usage_events WHERE feature_id={record.Id}") != null)
                    throw new ValidationException("Cannot change feature type after usage is recorded");
            }
            if (record.Id != 0 && record.ValueType == "metered" && await store.One($"SELECT id FROM subscrio.credit_consumption_rules WHERE feature_id={record.Id}") != null)
                throw new ValidationException("Remove credit consumption rules before changing to metered");
            if (record.Id != 0 && record.ValueType == "text")
                await store.Rows($"UPDATE subscrio.product_features SET composition_rule='override_wins',cross_subscription_rule=CASE WHEN cross_subscription_rule='legacy' THEN 'legacy' ELSE 'override_wins' END WHERE feature_id={record.Id}");
            await EfSaveHelper.SaveAsync(_db, _db.Features, record, r => r.Id);
            if (record.MeteredConfig != null)
                await new MeteredConfigRepository(store).SetMeteredConfigAsync(record.Key, record.MeteredConfig);
            await tx.CommitAsync();
            return record;
        });
    }

    public async Task<FeatureRecord?> FindByIdAsync(long id)
    {
        return await Hydrate(await _db.Features
            .FirstOrDefaultAsync(f => f.Id == id));
    }

    public async Task<FeatureRecord?> FindByKeyAsync(string key)
    {
        return await Hydrate(await _db.Features
            .FirstOrDefaultAsync(f => f.Key == key));
    }

    public async Task<List<FeatureRecord>> FindAllAsync(FeatureFilterDto? filters = null)
    {
        var query = _db.Features.AsQueryable();

        if (filters != null)
        {
            if (!string.IsNullOrEmpty(filters.Status))
            {
                query = query.Where(f => f.Status == filters.Status);
            }

            if (!string.IsNullOrEmpty(filters.ValueType))
            {
                query = query.Where(f => f.ValueType == filters.ValueType);
            }

            if (!string.IsNullOrEmpty(filters.GroupName))
            {
                query = query.Where(f => f.GroupName == filters.GroupName);
            }

            if (!string.IsNullOrEmpty(filters.Search))
            {
                var search = filters.Search;
                query = query.Where(f =>
                    EF.Functions.Like(f.Key, $"%{search}%") ||
                    EF.Functions.Like(f.DisplayName, $"%{search}%") ||
                    (f.Description != null && EF.Functions.Like(f.Description, $"%{search}%"))
                );
            }

            var sortBy = filters.SortBy ?? "createdAt";
            var sortOrder = filters.SortOrder ?? "asc";

            query = sortBy switch
            {
                "displayName" => sortOrder == "desc"
                    ? query.OrderByDescending(f => f.DisplayName)
                    : query.OrderBy(f => f.DisplayName),
                _ => sortOrder == "desc"
                    ? query.OrderByDescending(f => f.CreatedAt)
                    : query.OrderBy(f => f.CreatedAt)
            };

            query = query.ApplyPaging(filters.Offset, filters.Limit);
        }
        else
        {
            query = query.OrderBy(f => f.CreatedAt);
        }

        return await Hydrate(await query.ToListAsync());
    }

    public async Task<List<FeatureRecord>> FindByIdsAsync(List<long> ids)
    {
        if (ids.Count == 0)
            return new List<FeatureRecord>();

        return await Hydrate(await _db.Features
            .Where(f => ids.Contains(f.Id))
            .ToListAsync());
    }

    public async Task<List<FeatureRecord>> FindByProductAsync(long productId)
    {
        return await Hydrate(await _db.Features
            .Join(_db.ProductFeatures,
                f => f.Id,
                pf => pf.FeatureId,
                (f, pf) => new { Feature = f, ProductFeature = pf })
            .Where(x => x.ProductFeature.ProductId == productId)
            .Select(x => x.Feature)
            .OrderBy(f => f.CreatedAt)
            .ToListAsync());
    }

    public async Task DeleteAsync(long id)
    {
        await AccountingDelete.Run(_db, async () =>
        {
            var record = await _db.Features.FindAsync(id);
            if (record != null)
            {
                _db.Features.Remove(record);
                await _db.SaveChangesAsync();
            }

        });
    }

    public async Task<bool> HasProductAssociationsAsync(long featureId)
    {
        return await _db.ProductFeatures.AnyAsync(pf => pf.FeatureId == featureId);
    }

    public async Task<bool> HasPlanFeatureValuesAsync(long featureId)
    {
        return await _db.PlanFeatures.AnyAsync(pf => pf.FeatureId == featureId);
    }

    public async Task<bool> HasSubscriptionOverridesAsync(long featureId)
    {
        return await _db.SubscriptionFeatureOverrides.AnyAsync(sfo => sfo.FeatureId == featureId);
    }
}
