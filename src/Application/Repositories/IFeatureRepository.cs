using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Infrastructure.Database;

namespace Subscrio.Core.Application.Repositories;

public interface IFeatureRepository
{
    Task<FeatureRecord> SaveAsync(FeatureRecord feature);
    Task<FeatureRecord?> FindByIdAsync(long id);
    Task<FeatureRecord?> FindByKeyAsync(string key);
    Task<List<FeatureRecord>> FindAllAsync(FeatureFilterDto? filters = null);
    Task<List<FeatureRecord>> FindByIdsAsync(List<long> ids);
    Task DeleteAsync(long id);
    Task<bool> ExistsAsync(long id);

    // Get features by product
    Task<List<FeatureRecord>> FindByProductAsync(long productId);

    // Foreign key checks
    Task<bool> HasProductAssociationsAsync(long featureId);
    Task<bool> HasPlanFeatureValuesAsync(long featureId);
    Task<bool> HasSubscriptionOverridesAsync(long featureId);
}


