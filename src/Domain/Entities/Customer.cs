using Subscrio.Core.Domain.Base;
using Subscrio.Core.Domain.ValueObjects;
using Subscrio.Core.Infrastructure.Utils;

namespace Subscrio.Core.Domain.Entities;

public class CustomerProps
{
    public required string Key { get; init; }
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string? ExternalBillingId { get; set; }
    public required CustomerStatus Status { get; set; }
    public Dictionary<string, object?>? Metadata { get; set; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; set; }
}

public class Customer : Entity<CustomerProps>
{
    public Customer(CustomerProps props, long? id = null) : base(props, id)
    {
    }

    public string Key => Props.Key;

    public CustomerStatus Status => Props.Status;

    public string? ExternalBillingId => Props.ExternalBillingId;

    public void SetExternalBillingId(string? externalBillingId)
    {
        Props.ExternalBillingId = externalBillingId;
        Props.UpdatedAt = DateHelper.Now();
    }

    public void Archive()
    {
        Props.Status = CustomerStatus.Archived;
        Props.UpdatedAt = DateHelper.Now();
    }

    public void Unarchive()
    {
        Props.Status = CustomerStatus.Active;
        Props.UpdatedAt = DateHelper.Now();
    }

    public bool CanDelete()
    {
        return Props.Status == CustomerStatus.Archived;
    }
}
