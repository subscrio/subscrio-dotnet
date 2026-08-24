using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Infrastructure.Database;

namespace Subscrio.Core.Application.Repositories;

public interface ISubscriptionRepository
{
    Task<SubscriptionRecord> SaveAsync(SubscriptionRecord subscription);
    
    // Read methods return view records (for display)
    Task<SubscriptionStatusViewRecord?> FindByIdAsync(long id);
    Task<SubscriptionStatusViewRecord?> FindByKeyAsync(string key);
    Task<List<SubscriptionStatusViewRecord>> FindByCustomerIdAsync(long customerId, SubscriptionFilterDto? filters = null);
    Task<SubscriptionStatusViewRecord?> FindByStripeIdAsync(string stripeSubscriptionId);
    Task<List<SubscriptionWithCustomerRecord>> FindAllAsync(SubscriptionFilterDto? filters = null);
    Task<List<SubscriptionStatusViewRecord>> FindByIdsAsync(List<long> ids);
    
    // Update methods return table records (tracked, for modifications)
    Task<SubscriptionRecord?> FindByIdForUpdateAsync(long id);
    Task<SubscriptionRecord?> FindByKeyForUpdateAsync(string key);
    
    // Find active subscription for customer and plan combination
    Task<SubscriptionStatusViewRecord?> FindActiveByCustomerAndPlanAsync(long customerId, long planId);

    // Foreign key checks
    Task<bool> HasSubscriptionsForPlanAsync(long planId);
    Task<bool> HasSubscriptionsForBillingCycleAsync(long billingCycleId);

    // Find expired subscriptions with transition plans (for transition processing)
    Task<List<SubscriptionStatusViewRecord>> FindExpiredWithTransitionPlansAsync(int? limit = null);
    
    // Check if subscription has feature overrides
    Task<bool> HasFeatureOverridesAsync(long subscriptionId);
    
    // Feature override management
    Task AddFeatureOverrideAsync(long subscriptionId, long featureId, string value, string overrideType);
    Task RemoveFeatureOverrideAsync(long subscriptionId, long featureId);
    Task<List<SubscriptionFeatureOverrideRecord>> GetFeatureOverridesAsync(long subscriptionId);
    Task ClearTemporaryOverridesAsync(long subscriptionId);
    
    Task DeleteAsync(long id);
    Task<bool> ExistsAsync(long id);
}

public record SubscriptionWithCustomerRecord(SubscriptionStatusViewRecord Subscription, CustomerRecord? Customer);


