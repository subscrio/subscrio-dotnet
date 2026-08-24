using Subscrio.Core.Domain.Entities;
using Subscrio.Core.Domain.ValueObjects;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Infrastructure.Database;

namespace Subscrio.Core.Application.Mappers;

public static class CustomerMapper
{
    public static CustomerDto ToDto(Customer customer)
    {
        return new CustomerDto
        {
            Key = customer.Key,
            DisplayName = customer.Props.DisplayName,
            Email = customer.Props.Email,
            ExternalBillingId = customer.ExternalBillingId,
            Status = customer.Status.ToString().ToLowerInvariant(),
            Metadata = customer.Props.Metadata,
            CreatedAt = customer.Props.CreatedAt.ToUniversalTime().ToString("O"),
            UpdatedAt = customer.Props.UpdatedAt.ToUniversalTime().ToString("O")
        };
    }

    public static Customer ToDomain(CustomerRecord record)
    {
        return new Customer(
            new CustomerProps
            {
                Key = record.Key,
                DisplayName = record.DisplayName,
                Email = record.Email,
                ExternalBillingId = record.ExternalBillingId,
                Status = Enum.Parse<CustomerStatus>(record.Status, ignoreCase: true),
                Metadata = record.Metadata,
                CreatedAt = record.CreatedAt,
                UpdatedAt = record.UpdatedAt
            },
            record.Id
        );
    }

    public static CustomerRecord ToPersistence(Customer customer)
    {
        return new CustomerRecord
        {
            Id = customer.Id ?? 0,
            Key = customer.Key,
            DisplayName = customer.Props.DisplayName,
            Email = customer.Props.Email,
            ExternalBillingId = customer.ExternalBillingId,
            Status = customer.Status.ToString().ToLowerInvariant(),
            Metadata = customer.Props.Metadata,
            CreatedAt = customer.Props.CreatedAt,
            UpdatedAt = customer.Props.UpdatedAt
        };
    }
}


