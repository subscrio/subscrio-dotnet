using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Infrastructure.Database;

namespace Subscrio.Core.Application.Repositories;

public interface IPlanRepository
{
    Task<PlanRecord> SaveAsync(PlanRecord plan);
    Task<PlanRecord?> FindByIdAsync(long id);
    Task<PlanRecord?> FindByKeyAsync(string key);
    Task<List<PlanRecord>> FindByProductAsync(string productKey); // Uses productKey - joins to resolve
    Task<PlanRecord?> FindByBillingCycleIdAsync(long billingCycleId);
    Task<List<PlanRecord>> FindAllAsync(PlanFilterDto? filters = null);
    Task<List<PlanRecord>> FindByIdsAsync(List<long> ids);
    Task DeleteAsync(long id);
    Task<bool> ExistsAsync(long id);

    // Foreign key checks
    Task<bool> HasBillingCyclesAsync(long planId);
    Task<bool> HasPlanTransitionReferencesAsync(string billingCycleKey);

    // Plan-Feature value management
    Task SetFeatureValueAsync(long planId, long featureId, string value);
    Task RemoveFeatureValueAsync(long planId, long featureId);
    Task<string?> GetFeatureValueAsync(long planId, long featureId);
    Task<List<PlanFeatureRecord>> GetFeatureValuesAsync(long planId);
}


