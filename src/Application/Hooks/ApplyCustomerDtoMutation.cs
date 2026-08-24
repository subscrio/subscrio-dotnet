using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Application.Errors;
using Subscrio.Core.Infrastructure.Database;
using Subscrio.Core.Infrastructure.Utils;

namespace Subscrio.Core.Application.Hooks;

public static class ApplyCustomerDtoMutation
{
    public static void Apply(CustomerRecord record, CustomerDto dto, bool allowKeyChange = true)
    {
        if (dto.Key != record.Key)
        {
            if (!allowKeyChange)
            {
                throw new ValidationException("Customer key cannot be changed via hooks");
            }
            record.Key = dto.Key;
        }

        record.DisplayName = dto.DisplayName;
        record.Email = dto.Email;
        record.ExternalBillingId = dto.ExternalBillingId;
        record.Metadata = dto.Metadata;
        record.UpdatedAt = DateHelper.Now();
    }
}
