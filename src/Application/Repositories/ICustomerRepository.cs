using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Infrastructure.Database;

namespace Subscrio.Core.Application.Repositories;

public interface ICustomerRepository
{
    Task<CustomerRecord> SaveAsync(CustomerRecord customer);
    Task<CustomerRecord?> FindByIdAsync(long id);
    Task<CustomerRecord?> FindByKeyAsync(string key);
    Task<CustomerRecord?> FindByExternalBillingIdAsync(string externalBillingId);
    Task<List<CustomerRecord>> FindAllAsync(CustomerFilterDto? filters = null);
    Task DeleteAsync(long id);
    Task<bool> ExistsAsync(long id);
}


