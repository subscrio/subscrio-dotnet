using Subscrio.Core.Domain.Base;
using Subscrio.Core.Infrastructure.Utils;

namespace Subscrio.Core.Domain.Entities;

public class SystemConfigProps
{
    public required string ConfigKey { get; init; }
    public required string ConfigValue { get; set; }
    public required bool Encrypted { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; set; }
}

public class SystemConfig : Entity<SystemConfigProps>
{
    public SystemConfig(SystemConfigProps props, long? id = null) : base(props, id)
    {
    }

    public string ConfigKey => Props.ConfigKey;

    public string ConfigValue => Props.ConfigValue;

    public bool Encrypted => Props.Encrypted;

    public void UpdateValue(string value)
    {
        Props.ConfigValue = value;
        Props.UpdatedAt = DateHelper.Now();
    }
}
