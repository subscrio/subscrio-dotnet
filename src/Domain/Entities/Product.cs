using Subscrio.Core.Domain.Constants;
using Subscrio.Core.Domain.Errors;
using Subscrio.Core.Domain.Base;
using Subscrio.Core.Domain.ValueObjects;

namespace Subscrio.Core.Domain.Entities;

public class ProductProps
{
    public required string Key { get; init; }
    public required string DisplayName { get; set; }
    public string? Description { get; set; }
    public required ProductStatus Status { get; set; }
    public Dictionary<string, object?>? Metadata { get; set; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; set; }
}

public class Product : Entity<ProductProps>
{
    public Product(ProductProps props, long? id = null) : base(props, id)
    {
    }

    public string Key => Props.Key;

    public string DisplayName => Props.DisplayName;

    public ProductStatus Status => Props.Status;

    public void Archive()
    {
        Props.Status = ProductStatus.Archived;
        Props.UpdatedAt = DateTime.UtcNow;
    }

    public void Unarchive()
    {
        Props.Status = ProductStatus.Active;
        Props.UpdatedAt = DateTime.UtcNow;
    }

    public bool CanDelete()
    {
        return Props.Status == ProductStatus.Archived;
    }

    public void UpdateDisplayName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < DomainConstants.MinDisplayNameLength)
        {
            throw new DomainException($"Display name cannot be empty. Product key: {Key}");
        }
        if (name.Length > DomainConstants.MaxDisplayNameLength)
        {
            throw new DomainException($"Display name cannot exceed {DomainConstants.MaxDisplayNameLength} characters. Product key: {Key}, provided length: {name.Length}");
        }
        Props.DisplayName = name;
        Props.UpdatedAt = DateTime.UtcNow;
    }
}
