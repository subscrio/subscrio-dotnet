using Microsoft.EntityFrameworkCore;
using Subscrio.Core.Application.Repositories;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Infrastructure.Database;
using Subscrio.Core.Application.Errors;
using Subscrio.Core.Infrastructure.Utils;

namespace Subscrio.Core.Infrastructure.Repositories;

public class EfPlanRepository : IPlanRepository
{
    private readonly SubscrioDbContext _db;

    public EfPlanRepository(SubscrioDbContext db)
    {
        _db = db;
    }

    public async Task<PlanRecord> SaveAsync(PlanRecord record)
    {
        if (record.Id == 0)
        {
            // Insert new entity
            _db.Plans.Add(record);
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

    public async Task<PlanRecord?> FindByIdAsync(long id)
    {
        return await _db.Plans
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<PlanRecord?> FindByKeyAsync(string key)
    {
        return await _db.Plans
            .FirstOrDefaultAsync(p => p.Key == key);
    }

    public async Task<List<PlanRecord>> FindByProductAsync(string productKey)
    {
        var product = await _db.Products
            .FirstOrDefaultAsync(p => p.Key == productKey);

        if (product == null) return new List<PlanRecord>();

        return await _db.Plans
            .Where(p => p.ProductId == product.Id)
            .ToListAsync();
    }

    public async Task<PlanRecord?> FindByBillingCycleIdAsync(long billingCycleId)
    {
        return await _db.Plans
            .FirstOrDefaultAsync(p => p.OnExpireTransitionToBillingCycleId == billingCycleId);
    }

    public async Task<List<PlanRecord>> FindAllAsync(PlanFilterDto? filters = null)
    {
        var query = _db.Plans
            
            .Join(_db.Products,
                p => p.ProductId,
                pr => pr.Id,
                (p, pr) => new { Plan = p, ProductKey = pr.Key })
            .AsQueryable();

        if (filters != null)
        {
            if (!string.IsNullOrEmpty(filters.ProductKey))
            {
                query = query.Where(x => x.ProductKey == filters.ProductKey);
            }

            if (!string.IsNullOrEmpty(filters.Status))
            {
                query = query.Where(x => x.Plan.Status == filters.Status);
            }

            if (!string.IsNullOrEmpty(filters.Search))
            {
                var search = filters.Search;
                query = query.Where(x =>
                    EF.Functions.Like(x.Plan.DisplayName, $"%{search}%") ||
                    EF.Functions.Like(x.Plan.Key, $"%{search}%")
                );
            }

            var sortBy = filters.SortBy ?? "displayName";
            var sortOrder = filters.SortOrder ?? "asc";

            query = sortBy switch
            {
                "displayName" => sortOrder == "desc"
                    ? query.OrderByDescending(x => x.Plan.DisplayName)
                    : query.OrderBy(x => x.Plan.DisplayName),
                "createdAt" => sortOrder == "desc"
                    ? query.OrderByDescending(x => x.Plan.CreatedAt)
                    : query.OrderBy(x => x.Plan.CreatedAt),
                _ => query
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
            query = query.OrderBy(x => x.Plan.DisplayName);
        }

        var results = await query.ToListAsync();
        return results.Select(x => x.Plan).ToList();
    }

    public async Task<List<PlanRecord>> FindByIdsAsync(List<long> ids)
    {
        if (ids.Count == 0) return new List<PlanRecord>();

        return await _db.Plans
            .Where(p => ids.Contains(p.Id))
            .ToListAsync();
    }

    public async Task DeleteAsync(long id)
    {
        var record = await _db.Plans.FindAsync(id);
        if (record != null)
        {
            _db.Plans.Remove(record);
            await _db.SaveChangesAsync();
        }
    }

    public async Task<bool> ExistsAsync(long id)
    {
        return await _db.Plans.AnyAsync(p => p.Id == id);
    }

    public async Task<bool> HasBillingCyclesAsync(long planId)
    {
        return await _db.BillingCycles.AnyAsync(bc => bc.PlanId == planId);
    }

    public async Task<bool> HasPlanTransitionReferencesAsync(string billingCycleKey)
    {
        var billingCycle = await _db.BillingCycles
            
            .FirstOrDefaultAsync(bc => bc.Key == billingCycleKey);

        if (billingCycle == null) return false;

        return await _db.Plans.AnyAsync(p => p.OnExpireTransitionToBillingCycleId == billingCycle.Id);
    }

    public async Task SetFeatureValueAsync(long planId, long featureId, string value)
    {
        var existing = await _db.PlanFeatures
            .FirstOrDefaultAsync(pf => pf.PlanId == planId && pf.FeatureId == featureId);
        
        if (existing != null)
        {
            // Update existing
            existing.Value = value;
            existing.UpdatedAt = DateHelper.Now();
        }
        else
        {
            // Create new
            _db.PlanFeatures.Add(new PlanFeatureRecord
            {
                PlanId = planId,
                FeatureId = featureId,
                Value = value,
                CreatedAt = DateHelper.Now(),
                UpdatedAt = DateHelper.Now()
            });
        }
        await _db.SaveChangesAsync();
    }

    public async Task RemoveFeatureValueAsync(long planId, long featureId)
    {
        var record = await _db.PlanFeatures
            .FirstOrDefaultAsync(pf => pf.PlanId == planId && pf.FeatureId == featureId);
        
        if (record != null)
        {
            _db.PlanFeatures.Remove(record);
            await _db.SaveChangesAsync();
        }
    }

    public async Task<string?> GetFeatureValueAsync(long planId, long featureId)
    {
        var record = await _db.PlanFeatures
            .FirstOrDefaultAsync(pf => pf.PlanId == planId && pf.FeatureId == featureId);
        
        return record?.Value;
    }

    public async Task<List<PlanFeatureRecord>> GetFeatureValuesAsync(long planId)
    {
        return await _db.PlanFeatures
            .Where(pf => pf.PlanId == planId)
            .ToListAsync();
    }

}

