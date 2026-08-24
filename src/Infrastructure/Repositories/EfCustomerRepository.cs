using Microsoft.EntityFrameworkCore;
using Subscrio.Core.Application.Repositories;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Infrastructure.Database;

namespace Subscrio.Core.Infrastructure.Repositories;

public class EfCustomerRepository : ICustomerRepository
{
    private readonly SubscrioDbContext _db;

    public EfCustomerRepository(SubscrioDbContext db)
    {
        _db = db;
    }

    public async Task<CustomerRecord> SaveAsync(CustomerRecord record)
    {
        if (record.Id == 0)
        {
            _db.Customers.Add(record);
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

    public async Task<CustomerRecord?> FindByIdAsync(long id)
    {
        return await _db.Customers
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<CustomerRecord?> FindByKeyAsync(string key)
    {
        return await _db.Customers
            .FirstOrDefaultAsync(c => c.Key == key);
    }

    public async Task<CustomerRecord?> FindByExternalBillingIdAsync(string externalBillingId)
    {
        return await _db.Customers
            .FirstOrDefaultAsync(c => c.ExternalBillingId == externalBillingId);
    }

    public async Task<List<CustomerRecord>> FindAllAsync(CustomerFilterDto? filters = null)
    {
        var query = _db.Customers.AsQueryable();

        if (filters != null)
        {
            if (!string.IsNullOrEmpty(filters.Status))
            {
                query = query.Where(c => c.Status == filters.Status);
            }

            if (!string.IsNullOrEmpty(filters.Search))
            {
                var search = filters.Search;
                query = query.Where(c =>
                    EF.Functions.Like(c.Key, $"%{search}%") ||
                    (c.DisplayName != null && EF.Functions.Like(c.DisplayName, $"%{search}%")) ||
                    (c.Email != null && EF.Functions.Like(c.Email, $"%{search}%"))
                );
            }

            var sortBy = filters.SortBy ?? "createdAt";
            var sortOrder = filters.SortOrder ?? "desc";

            query = sortBy switch
            {
                "displayName" => sortOrder == "desc"
                    ? query.OrderByDescending(c => c.DisplayName)
                    : query.OrderBy(c => c.DisplayName),
                "key" => sortOrder == "desc"
                    ? query.OrderByDescending(c => c.Key)
                    : query.OrderBy(c => c.Key),
                _ => sortOrder == "desc"
                    ? query.OrderByDescending(c => c.CreatedAt)
                    : query.OrderBy(c => c.CreatedAt)
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
            query = query.OrderByDescending(c => c.CreatedAt);
        }

        return await query.ToListAsync();
    }

    public async Task DeleteAsync(long id)
    {
        var record = await _db.Customers.FindAsync(id);
        if (record != null)
        {
            _db.Customers.Remove(record);
            await _db.SaveChangesAsync();
        }
    }

    public async Task<bool> ExistsAsync(long id)
    {
        return await _db.Customers.AnyAsync(c => c.Id == id);
    }
}

