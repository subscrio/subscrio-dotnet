using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Infrastructure.Database;

namespace Subscrio.Core.Application.Repositories;

public interface IProductRepository
{
    Task<ProductRecord> SaveAsync(ProductRecord product);
    Task<ProductRecord?> FindByIdAsync(long id);
    Task<ProductRecord?> FindByKeyAsync(string key);
    Task<List<ProductRecord>> FindAllAsync(ProductFilterDto? filters = null);
    Task<List<ProductRecord>> FindByIdsAsync(List<long> ids);
    Task DeleteAsync(long id);
    Task<bool> ExistsAsync(long id);

    // Product-Feature associations
    Task AssociateFeatureAsync(long productId, long featureId);
    Task DissociateFeatureAsync(long productId, long featureId);
    Task<List<long>> GetFeaturesByProductAsync(long productId);

    // Foreign key checks
    Task<bool> HasPlansAsync(string productKey);
}


