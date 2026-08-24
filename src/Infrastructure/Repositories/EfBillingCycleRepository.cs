using Microsoft.EntityFrameworkCore;
using Subscrio.Core.Application.Repositories;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Infrastructure.Database;

namespace Subscrio.Core.Infrastructure.Repositories;

public class EfBillingCycleRepository : IBillingCycleRepository
{
    private readonly SubscrioDbContext _db;

    public EfBillingCycleRepository(SubscrioDbContext db)
    {
        _db = db;
    }

    public async Task<BillingCycleRecord> SaveAsync(BillingCycleRecord record)
    {
        if (record.Id == 0)
        {
            _db.BillingCycles.Add(record);
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

    public async Task<BillingCycleRecord?> FindByIdAsync(long id)
    {
        return await _db.BillingCycles
            .FirstOrDefaultAsync(bc => bc.Id == id);
    }

    public async Task<BillingCycleRecord?> FindByKeyAsync(string key)
    {
        return await _db.BillingCycles
            .FirstOrDefaultAsync(bc => bc.Key == key);
    }

    public async Task<List<BillingCycleRecord>> FindByPlanAsync(long planId)
    {
        return await _db.BillingCycles
            .Where(bc => bc.PlanId == planId)
            .OrderBy(bc => bc.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<BillingCycleRecord>> FindAllAsync(BillingCycleFilterDto? filters = null)
    {
        var query = _db.BillingCycles.AsQueryable();

        if (filters != null)
        {
            if (!string.IsNullOrEmpty(filters.PlanKey))
            {
                // Join with plans to filter by planKey
                var plan = await _db.Plans
                    .FirstOrDefaultAsync(p => p.Key == filters.PlanKey);

                if (plan != null)
                {
                    query = query.Where(bc => bc.PlanId == plan.Id);
                }
                else
                {
                    // Plan not found, return empty list
                    return new List<BillingCycleRecord>();
                }
            }

            if (!string.IsNullOrEmpty(filters.Status))
            {
                query = query.Where(bc => bc.Status == filters.Status);
            }

            if (!string.IsNullOrEmpty(filters.DurationUnit))
            {
                query = query.Where(bc => bc.DurationUnit == filters.DurationUnit);
            }

            if (!string.IsNullOrEmpty(filters.Search))
            {
                var search = filters.Search;
                query = query.Where(bc =>
                    EF.Functions.Like(bc.DisplayName, $"%{search}%") ||
                    (bc.Description != null && EF.Functions.Like(bc.Description, $"%{search}%"))
                );
            }

            var sortBy = filters.SortBy ?? "displayOrder";
            var sortOrder = filters.SortOrder ?? "asc";

            query = sortBy switch
            {
                "displayName" => sortOrder == "desc"
                    ? query.OrderByDescending(bc => bc.DisplayName)
                    : query.OrderBy(bc => bc.DisplayName),
                _ => sortOrder == "desc"
                    ? query.OrderByDescending(bc => bc.CreatedAt)
                    : query.OrderBy(bc => bc.CreatedAt)
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
            query = query.OrderBy(bc => bc.CreatedAt);
        }

        return await query.ToListAsync();
    }

    public async Task DeleteAsync(long id)
    {
        var record = await _db.BillingCycles.FindAsync(id);
        if (record != null)
        {
            _db.BillingCycles.Remove(record);
            await _db.SaveChangesAsync();
        }
    }

    public async Task<bool> ExistsAsync(long id)
    {
        return await _db.BillingCycles.AnyAsync(bc => bc.Id == id);
    }
}

