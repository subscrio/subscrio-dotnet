using Microsoft.EntityFrameworkCore;
using Subscrio.Core.Application.Repositories;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Infrastructure.Database;

namespace Subscrio.Core.Infrastructure.Repositories;

public class EfSubscriptionRepository : ISubscriptionRepository
{
    private readonly SubscrioDbContext _db;

    public EfSubscriptionRepository(SubscrioDbContext db)
    {
        _db = db;
    }

    public async Task<SubscriptionRecord> SaveAsync(SubscriptionRecord record)
    {
        if (record.Id == 0)
        {
            // Insert new entity
            _db.Subscriptions.Add(record);
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

    // Read methods return view records (for display)
    public async Task<SubscriptionStatusViewRecord?> FindByIdAsync(long id)
    {
        return await _db.SubscriptionStatusView
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<SubscriptionStatusViewRecord?> FindByKeyAsync(string key)
    {
        return await _db.SubscriptionStatusView
            .FirstOrDefaultAsync(s => s.Key == key);
    }

    public async Task<List<SubscriptionStatusViewRecord>> FindByCustomerIdAsync(long customerId, SubscriptionFilterDto? filters = null)
    {
        var query = _db.SubscriptionStatusView
            .Where(s => s.CustomerId == customerId)
            .AsQueryable();

        if (filters != null)
        {
            if (!string.IsNullOrEmpty(filters.Status))
            {
                query = query.Where(s => s.ComputedStatus.ToLower() == filters.Status.ToLower());
            }

            if (filters.IsArchived.HasValue)
            {
                query = query.Where(s => s.IsArchived == filters.IsArchived.Value);
            }
        }

        query = query.OrderByDescending(s => s.CreatedAt);

        if (filters?.Offset > 0)
        {
            query = query.Skip(filters.Offset.Value);
        }

        if (filters?.Limit > 0)
        {
            query = query.Take(filters.Limit.Value);
        }

        return await query.ToListAsync();
    }

    public async Task<SubscriptionStatusViewRecord?> FindByStripeIdAsync(string stripeSubscriptionId)
    {
        // Only find active (non-archived) subscriptions by Stripe ID
        return await _db.SubscriptionStatusView
            .Where(s => s.StripeSubscriptionId == stripeSubscriptionId && !s.IsArchived)
            .FirstOrDefaultAsync();
    }

    public async Task<List<SubscriptionWithCustomerRecord>> FindAllAsync(SubscriptionFilterDto? filters = null)
    {
        var query = _db.SubscriptionStatusView
            .Join(_db.Customers,
                s => s.CustomerId,
                c => c.Id,
                (s, c) => new { Subscription = s, Customer = c })
            .AsQueryable();

        if (filters != null)
        {
            if (!string.IsNullOrEmpty(filters.CustomerKey))
            {
                query = query.Where(x => x.Customer.Key == filters.CustomerKey);
            }

            if (!string.IsNullOrEmpty(filters.PlanKey))
            {
                query = query.Join(_db.Plans,
                    x => x.Subscription.PlanId,
                    p => p.Id,
                    (x, p) => new { x.Subscription, x.Customer, Plan = p })
                    .Where(x => x.Plan.Key == filters.PlanKey)
                    .Select(x => new { x.Subscription, x.Customer });
            }

            if (!string.IsNullOrEmpty(filters.ProductKey))
            {
                query = query.Join(_db.Plans,
                        x => x.Subscription.PlanId,
                        p => p.Id,
                        (x, p) => new { x.Subscription, x.Customer, Plan = p })
                    .Join(_db.Products,
                        x => x.Plan.ProductId,
                        prod => prod.Id,
                        (x, prod) => new { x.Subscription, x.Customer, Product = prod })
                    .Where(x => x.Product.Key == filters.ProductKey)
                    .Select(x => new { x.Subscription, x.Customer });
            }

            if (!string.IsNullOrEmpty(filters.Status))
            {
                query = query.Where(x => x.Subscription.ComputedStatus.ToLower() == filters.Status.ToLower());
            }

            if (filters.IsArchived.HasValue)
            {
                query = query.Where(x => x.Subscription.IsArchived == filters.IsArchived.Value);
            }
        }

        query = query.OrderByDescending(x => x.Subscription.CreatedAt);

        if (filters?.Offset > 0)
        {
            query = query.Skip(filters.Offset.Value);
        }

        if (filters?.Limit > 0)
        {
            query = query.Take(filters.Limit.Value);
        }

        var results = await query.ToListAsync();
        return results.Select(x => new SubscriptionWithCustomerRecord(x.Subscription, x.Customer)).ToList();
    }

    public async Task<List<SubscriptionWithCustomerRecord>> FindDetailedAsync(
        DetailedSubscriptionFilterDto filters,
        Dictionary<string, object?>? resolvedFilters = null)
    {
        var query = _db.SubscriptionStatusView
            .Join(_db.Customers,
                s => s.CustomerId,
                c => c.Id,
                (s, c) => new { Subscription = s, Customer = c })
            .AsQueryable();

        if (!string.IsNullOrEmpty(filters.CustomerKey))
        {
            query = query.Where(x => x.Customer.Key == filters.CustomerKey);
        }

        if (!string.IsNullOrEmpty(filters.PlanKey))
        {
            query = query.Join(_db.Plans,
                    x => x.Subscription.PlanId,
                    p => p.Id,
                    (x, p) => new { x.Subscription, x.Customer, Plan = p })
                .Where(x => x.Plan.Key == filters.PlanKey)
                .Select(x => new { x.Subscription, x.Customer });
        }

        if (!string.IsNullOrEmpty(filters.ProductKey) ||
            (resolvedFilters != null && resolvedFilters.TryGetValue("planIds", out var planIdsObj) && planIdsObj is List<long> { Count: > 0 }))
        {
            if (resolvedFilters != null && resolvedFilters.TryGetValue("planIds", out var idsObj) && idsObj is List<long> planIds && planIds.Count > 0)
            {
                query = query.Where(x => planIds.Contains(x.Subscription.PlanId));
            }
            else if (!string.IsNullOrEmpty(filters.ProductKey))
            {
                query = query.Join(_db.Plans,
                        x => x.Subscription.PlanId,
                        p => p.Id,
                        (x, p) => new { x.Subscription, x.Customer, Plan = p })
                    .Join(_db.Products,
                        x => x.Plan.ProductId,
                        prod => prod.Id,
                        (x, prod) => new { x.Subscription, x.Customer, Product = prod })
                    .Where(x => x.Product.Key == filters.ProductKey)
                    .Select(x => new { x.Subscription, x.Customer });
            }
        }

        if (!string.IsNullOrEmpty(filters.BillingCycleKey) ||
            (resolvedFilters != null && resolvedFilters.TryGetValue("billingCycleId", out _)))
        {
            if (resolvedFilters != null && resolvedFilters.TryGetValue("billingCycleId", out var bcObj) && bcObj is long billingCycleId)
            {
                query = query.Where(x => x.Subscription.BillingCycleId == billingCycleId);
            }
            else if (!string.IsNullOrEmpty(filters.BillingCycleKey))
            {
                query = query.Join(_db.BillingCycles,
                        x => x.Subscription.BillingCycleId,
                        bc => bc.Id,
                        (x, bc) => new { x.Subscription, x.Customer, BillingCycle = bc })
                    .Where(x => x.BillingCycle.Key == filters.BillingCycleKey)
                    .Select(x => new { x.Subscription, x.Customer });
            }
        }

        if (!string.IsNullOrEmpty(filters.Status))
        {
            query = query.Where(x => x.Subscription.ComputedStatus.ToLower() == filters.Status.ToLower());
        }

        if (filters.IsArchived.HasValue)
        {
            query = query.Where(x => x.Subscription.IsArchived == filters.IsArchived.Value);
        }

        if (filters.ActivationDateFrom.HasValue)
            query = query.Where(x => x.Subscription.ActivationDate >= filters.ActivationDateFrom.Value);
        if (filters.ActivationDateTo.HasValue)
            query = query.Where(x => x.Subscription.ActivationDate <= filters.ActivationDateTo.Value);
        if (filters.ExpirationDateFrom.HasValue)
            query = query.Where(x => x.Subscription.ExpirationDate >= filters.ExpirationDateFrom.Value);
        if (filters.ExpirationDateTo.HasValue)
            query = query.Where(x => x.Subscription.ExpirationDate <= filters.ExpirationDateTo.Value);
        if (filters.TrialEndDateFrom.HasValue)
            query = query.Where(x => x.Subscription.TrialEndDate >= filters.TrialEndDateFrom.Value);
        if (filters.TrialEndDateTo.HasValue)
            query = query.Where(x => x.Subscription.TrialEndDate <= filters.TrialEndDateTo.Value);
        if (filters.CurrentPeriodStartFrom.HasValue)
            query = query.Where(x => x.Subscription.CurrentPeriodStart >= filters.CurrentPeriodStartFrom.Value);
        if (filters.CurrentPeriodStartTo.HasValue)
            query = query.Where(x => x.Subscription.CurrentPeriodStart <= filters.CurrentPeriodStartTo.Value);
        if (filters.CurrentPeriodEndFrom.HasValue)
            query = query.Where(x => x.Subscription.CurrentPeriodEnd >= filters.CurrentPeriodEndFrom.Value);
        if (filters.CurrentPeriodEndTo.HasValue)
            query = query.Where(x => x.Subscription.CurrentPeriodEnd <= filters.CurrentPeriodEndTo.Value);

        if (filters.HasStripeId == true)
            query = query.Where(x => x.Subscription.StripeSubscriptionId != null && x.Subscription.StripeSubscriptionId != "");
        else if (filters.HasStripeId == false)
            query = query.Where(x => x.Subscription.StripeSubscriptionId == null || x.Subscription.StripeSubscriptionId == "");

        if (filters.HasTrial == true)
            query = query.Where(x => x.Subscription.TrialEndDate != null);
        else if (filters.HasTrial == false)
            query = query.Where(x => x.Subscription.TrialEndDate == null);

        query = query.OrderByDescending(x => x.Subscription.CreatedAt);

        if (filters.Offset > 0)
            query = query.Skip(filters.Offset.Value);
        if (filters.Limit > 0)
            query = query.Take(filters.Limit.Value);

        var results = await query.ToListAsync();
        return results.Select(x => new SubscriptionWithCustomerRecord(x.Subscription, x.Customer)).ToList();
    }

    public async Task<List<SubscriptionStatusViewRecord>> FindByIdsAsync(List<long> ids)
    {
        if (ids.Count == 0) return new List<SubscriptionStatusViewRecord>();

        return await _db.SubscriptionStatusView
            .Where(s => ids.Contains(s.Id))
            .ToListAsync();
    }

    // Update methods return table records (tracked, for modifications)
    public async Task<SubscriptionRecord?> FindByIdForUpdateAsync(long id)
    {
        return await _db.Subscriptions
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<SubscriptionRecord?> FindByKeyForUpdateAsync(string key)
    {
        return await _db.Subscriptions
            .FirstOrDefaultAsync(s => s.Key == key);
    }

    public async Task<SubscriptionStatusViewRecord?> FindActiveByCustomerAndPlanAsync(long customerId, long planId)
    {
        return await _db.SubscriptionStatusView
            .Where(s => 
                s.CustomerId == customerId && 
                s.PlanId == planId &&
                (s.ComputedStatus.ToLower() == "active" || s.ComputedStatus.ToLower() == "trial"))
            .FirstOrDefaultAsync();
    }

    public async Task<bool> HasSubscriptionsForPlanAsync(long planId)
    {
        return await _db.Subscriptions.AnyAsync(s => s.PlanId == planId);
    }

    public async Task<bool> HasSubscriptionsForBillingCycleAsync(long billingCycleId)
    {
        return await _db.Subscriptions.AnyAsync(s => s.BillingCycleId == billingCycleId);
    }

    public async Task<List<SubscriptionStatusViewRecord>> FindExpiredWithTransitionPlansAsync(int? limit = null)
    {
        var query = _db.SubscriptionStatusView
            .Where(s => 
                s.ComputedStatus.ToLower() == "expired" &&
                !s.IsArchived)
            .Join(_db.Plans,
                s => s.PlanId,
                p => p.Id,
                (s, p) => new { Subscription = s, Plan = p })
            .Where(x => x.Plan.OnExpireTransitionToBillingCycleId != null)
            .Select(x => x.Subscription)
            .AsQueryable();

        if (limit.HasValue && limit.Value > 0)
        {
            query = query.Take(limit.Value);
        }

        return await query
            .OrderByDescending(s => s.ExpirationDate)
            .ToListAsync();
    }

    public async Task<bool> HasFeatureOverridesAsync(long subscriptionId)
    {
        return await _db.SubscriptionFeatureOverrides
            .AnyAsync(sfo => sfo.SubscriptionId == subscriptionId);
    }

    public async Task AddFeatureOverrideAsync(long subscriptionId, long featureId, string value, string overrideType)
    {
        // Check if override already exists
        var existing = await _db.SubscriptionFeatureOverrides
            .FirstOrDefaultAsync(sfo => sfo.SubscriptionId == subscriptionId && sfo.FeatureId == featureId);
        
        if (existing != null)
        {
            // Update existing override
            existing.Value = value;
            existing.OverrideType = overrideType;
        }
        else
        {
            // Create new override
            _db.SubscriptionFeatureOverrides.Add(new SubscriptionFeatureOverrideRecord
            {
                SubscriptionId = subscriptionId,
                FeatureId = featureId,
                Value = value,
                OverrideType = overrideType,
                CreatedAt = DateTime.UtcNow
            });
        }
        
        await _db.SaveChangesAsync();
    }

    public async Task RemoveFeatureOverrideAsync(long subscriptionId, long featureId)
    {
        var existing = await _db.SubscriptionFeatureOverrides
            .FirstOrDefaultAsync(sfo => sfo.SubscriptionId == subscriptionId && sfo.FeatureId == featureId);
        
        if (existing != null)
        {
            _db.SubscriptionFeatureOverrides.Remove(existing);
            await _db.SaveChangesAsync();
        }
    }

    public async Task<List<SubscriptionFeatureOverrideRecord>> GetFeatureOverridesAsync(long subscriptionId)
    {
        return await _db.SubscriptionFeatureOverrides
            .Where(sfo => sfo.SubscriptionId == subscriptionId)
            .ToListAsync();
    }

    public async Task ClearTemporaryOverridesAsync(long subscriptionId)
    {
        var temporaryOverrides = await _db.SubscriptionFeatureOverrides
            .Where(sfo => sfo.SubscriptionId == subscriptionId && sfo.OverrideType == "temporary")
            .ToListAsync();
        
        if (temporaryOverrides.Count > 0)
        {
            _db.SubscriptionFeatureOverrides.RemoveRange(temporaryOverrides);
            await _db.SaveChangesAsync();
        }
    }

    public async Task DeleteAsync(long id)
    {
        var record = await _db.Subscriptions.FindAsync(id);
        if (record != null)
        {
            _db.Subscriptions.Remove(record);
            await _db.SaveChangesAsync();
        }
    }

    public async Task<bool> ExistsAsync(long id)
    {
        return await _db.Subscriptions.AnyAsync(s => s.Id == id);
    }
}
