using Subscrio.Core.Domain.Entities;
using Subscrio.Core.Domain.ValueObjects;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Infrastructure.Database;

namespace Subscrio.Core.Application.Mappers;

public static class ProductMapper
{
    public static ProductDto ToDto(Product product)
    {
        return new ProductDto(
            Key: product.Key,
            DisplayName: product.DisplayName,
            Description: product.Props.Description,
            Status: product.Status.ToString().ToLowerInvariant(),
            Metadata: product.Props.Metadata,
            CreatedAt: product.Props.CreatedAt.ToUniversalTime().ToString("O"),
            UpdatedAt: product.Props.UpdatedAt.ToUniversalTime().ToString("O")
        );
    }

    public static Product ToDomain(ProductRecord record)
    {
        return new Product(
            new ProductProps
            {
                Key = record.Key,
                DisplayName = record.DisplayName,
                Description = record.Description,
                Status = Enum.Parse<ProductStatus>(record.Status, ignoreCase: true),
                Metadata = record.Metadata,
                CreatedAt = record.CreatedAt,
                UpdatedAt = record.UpdatedAt
            },
            record.Id
        );
    }

    public static ProductRecord ToPersistence(Product product)
    {
        return new ProductRecord
        {
            Id = product.Id ?? 0, // Will be set by EF Core on insert
            Key = product.Key,
            DisplayName = product.DisplayName,
            Description = product.Props.Description,
            Status = product.Status.ToString().ToLowerInvariant(),
            Metadata = product.Props.Metadata,
            CreatedAt = product.Props.CreatedAt,
            UpdatedAt = product.Props.UpdatedAt
        };
    }
}

