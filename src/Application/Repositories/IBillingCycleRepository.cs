using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Infrastructure.Database;

namespace Subscrio.Core.Application.Repositories;

public interface IBillingCycleRepository
{
    Task<BillingCycleRecord> SaveAsync(BillingCycleRecord billingCycle);
    Task<BillingCycleRecord?> FindByIdAsync(long id);
    Task<BillingCycleRecord?> FindByKeyAsync(string key);
    Task<List<BillingCycleRecord>> FindByPlanAsync(long planId);
    Task<List<BillingCycleRecord>> FindAllAsync(BillingCycleFilterDto? filters = null);
    Task DeleteAsync(long id);
    Task<bool> ExistsAsync(long id);
}


