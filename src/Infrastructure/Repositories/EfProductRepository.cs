using Microsoft.EntityFrameworkCore;
using Subscrio.Core.Application.Repositories;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Infrastructure.Database;
using Subscrio.Core.Infrastructure.Utils;

namespace Subscrio.Core.Infrastructure.Repositories;

public class EfProductRepository : IProductRepository
{
    private readonly SubscrioDbContext _db;

    public EfProductRepository(SubscrioDbContext db)
    {
        _db = db;
    }

    public async Task<ProductRecord> SaveAsync(ProductRecord record)
    {
        if (record.Id == 0)
        {
            // Insert new entity
            _db.Products.Add(record);
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

    public async Task<ProductRecord?> FindByIdAsync(long id)
    {
        return await _db.Products
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<ProductRecord?> FindByKeyAsync(string key)
    {
        return await _db.Products
            .FirstOrDefaultAsync(p => p.Key == key);
    }

    public async Task<List<ProductRecord>> FindByIdsAsync(List<long> ids)
    {
        if (ids.Count == 0) return new List<ProductRecord>();

        return await _db.Products
            .Where(p => ids.Contains(p.Id))
            .ToListAsync();
    }

    public async Task<List<ProductRecord>> FindAllAsync(ProductFilterDto? filters = null)
    {
        var query = _db.Products.AsQueryable();

        if (filters?.Status != null)
        {
            query = query.Where(p => p.Status == filters.Status);
        }

        if (!string.IsNullOrEmpty(filters?.Search))
        {
            var search = filters.Search;
            query = query.Where(p => 
                EF.Functions.Like(p.DisplayName, $"%{search}%") ||
                EF.Functions.Like(p.Key, $"%{search}%")
            );
        }

        if (filters?.SortBy != null)
        {
            query = filters.SortBy switch
            {
                "displayName" => filters.SortOrder == "desc" 
                    ? query.OrderByDescending(p => p.DisplayName)
                    : query.OrderBy(p => p.DisplayName),
                "createdAt" => filters.SortOrder == "desc"
                    ? query.OrderByDescending(p => p.CreatedAt)
                    : query.OrderBy(p => p.CreatedAt),
                _ => query
            };
        }
        else
        {
            query = query.OrderBy(p => p.DisplayName);
        }

        if (filters?.Limit > 0)
        {
            query = query.Take(filters.Limit);
        }

        if (filters?.Offset > 0)
        {
            query = query.Skip(filters.Offset);
        }

        return await query.ToListAsync();
    }

    public async Task DeleteAsync(long id)
    {
        var record = await _db.Products.FindAsync(id);
        if (record != null)
        {
            _db.Products.Remove(record);
            await _db.SaveChangesAsync();
        }
    }

    public async Task<bool> ExistsAsync(long id)
    {
        return await _db.Products.AnyAsync(p => p.Id == id);
    }

    public async Task AssociateFeatureAsync(long productId, long featureId)
    {
        // Check if association already exists
        var exists = await _db.ProductFeatures
            .AnyAsync(pf => pf.ProductId == productId && pf.FeatureId == featureId);

        if (!exists)
        {
            _db.ProductFeatures.Add(new ProductFeatureRecord
            {
                ProductId = productId,
                FeatureId = featureId,
                CreatedAt = DateHelper.Now()
            });
            await _db.SaveChangesAsync();
        }
    }

    public async Task DissociateFeatureAsync(long productId, long featureId)
    {
        var record = await _db.ProductFeatures
            .FirstOrDefaultAsync(pf => pf.ProductId == productId && pf.FeatureId == featureId);

        if (record != null)
        {
            _db.ProductFeatures.Remove(record);
            await _db.SaveChangesAsync();
        }
    }

    public async Task<List<long>> GetFeaturesByProductAsync(long productId)
    {
        return await _db.ProductFeatures
            .Where(pf => pf.ProductId == productId)
            .Select(pf => pf.FeatureId)
            .ToListAsync();
    }

    public async Task<bool> HasPlansAsync(string productKey)
    {
        var product = await _db.Products
            .FirstOrDefaultAsync(p => p.Key == productKey);

        if (product == null) return false;

        return await _db.Plans.AnyAsync(p => p.ProductId == product.Id);
    }
}

