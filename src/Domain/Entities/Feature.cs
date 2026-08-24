using Subscrio.Core.Application.Errors;
using Subscrio.Core.Domain.Base;
using Subscrio.Core.Domain.ValueObjects;
using Subscrio.Core.Infrastructure.Utils;

namespace Subscrio.Core.Domain.Entities;

public class FeatureProps
{
    public required string Key { get; init; }
    public required string DisplayName { get; set; }
    public string? Description { get; set; }
    public required FeatureValueType ValueType { get; init; }
    public required string DefaultValue { get; set; }
    public string? GroupName { get; set; }
    public required FeatureStatus Status { get; set; }
    public Dictionary<string, object?>? Validator { get; set; }
    public Dictionary<string, object?>? Metadata { get; set; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; set; }
}

public class Feature : Entity<FeatureProps>
{
    public Feature(FeatureProps props, long? id = null) : base(props, id)
    {
    }

    public string Key => Props.Key;

    public string DisplayName => Props.DisplayName;

    public FeatureStatus Status => Props.Status;

    public FeatureValueType ValueType => Props.ValueType;

    public string DefaultValue => Props.DefaultValue;

    public void Archive()
    {
        Props.Status = FeatureStatus.Archived;
        Props.UpdatedAt = DateHelper.Now();
    }

    public void Unarchive()
    {
        Props.Status = FeatureStatus.Active;
        Props.UpdatedAt = DateHelper.Now();
    }

    public bool CanDelete()
    {
        return Props.Status == FeatureStatus.Archived;
    }

    public void UpdateDisplayName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length == 0)
        {
            throw new DomainException("Display name cannot be empty");
        }
        Props.DisplayName = name;
        Props.UpdatedAt = DateHelper.Now();
    }
}
