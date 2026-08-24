using Microsoft.EntityFrameworkCore;
using Subscrio.Core.Application.Repositories;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Infrastructure.Database;

namespace Subscrio.Core.Infrastructure.Repositories;

public class EfFeatureRepository : IFeatureRepository
{
    private readonly SubscrioDbContext _db;

    public EfFeatureRepository(SubscrioDbContext db)
    {
        _db = db;
    }

    public async Task<FeatureRecord> SaveAsync(FeatureRecord record)
    {
        if (record.Id == 0)
        {
            _db.Features.Add(record);
            await _db.SaveChangesAsync();
            return record;
        }
        else
        {
            // Update existing - record is already tracked from service layer
            // EF Core automatically detects changes
            await _db.SaveChangesAsync();
            return record;
        }
    }

    public async Task<FeatureRecord?> FindByIdAsync(long id)
    {
        return await _db.Features
            .FirstOrDefaultAsync(f => f.Id == id);
    }

    public async Task<FeatureRecord?> FindByKeyAsync(string key)
    {
        return await _db.Features
            .FirstOrDefaultAsync(f => f.Key == key);
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

            if (filters.Limit > 0)
            {
                query = query.Take(filters.Limit);
            }

            if (filters.Offset > 0)
            {
                query = query.Skip(filters.Offset);
            }
        }
        else
        {
            query = query.OrderBy(f => f.CreatedAt);
        }

        return await query.ToListAsync();
    }

    public async Task<List<FeatureRecord>> FindByIdsAsync(List<long> ids)
    {
        if (ids.Count == 0) return new List<FeatureRecord>();

        return await _db.Features
            .Where(f => ids.Contains(f.Id))
            .ToListAsync();
    }

    public async Task<List<FeatureRecord>> FindByProductAsync(long productId)
    {
        return await _db.Features
            .Join(_db.ProductFeatures,
                f => f.Id,
                pf => pf.FeatureId,
                (f, pf) => new { Feature = f, ProductFeature = pf })
            .Where(x => x.ProductFeature.ProductId == productId)
            .Select(x => x.Feature)
            .OrderBy(f => f.CreatedAt)
            .ToListAsync();
    }

    public async Task DeleteAsync(long id)
    {
        var record = await _db.Features.FindAsync(id);
        if (record != null)
        {
            _db.Features.Remove(record);
            await _db.SaveChangesAsync();
        }
    }

    public async Task<bool> ExistsAsync(long id)
    {
        return await _db.Features.AnyAsync(f => f.Id == id);
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

