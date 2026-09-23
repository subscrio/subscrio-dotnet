namespace Subscrio.Core.Application.DTOs;

public record CreateAddonDto(string Key, string ProductKey, string DisplayName, string? Description = null, string CompositionMode = "additive", int Priority = 0, Dictionary<string, object?>? Metadata = null, Dictionary<string, string>? FeatureValues = null);
public record UpdateAddonDto(string? DisplayName = null, string? Description = null, string? CompositionMode = null, int? Priority = null, Dictionary<string, object?>? Metadata = null, Dictionary<string, string?>? FeatureValues = null);
public record AddonDto(string Key, string ProductKey, string DisplayName, string? Description, string CompositionMode, int Priority, string Status, Dictionary<string, object?>? Metadata, string CreatedAt, string UpdatedAt)
{
    public Dictionary<string, string> FeatureValues { get; init; } = new();
}
public record SubscriptionAddonDto(string SubscriptionKey, string AddonKey, int Quantity, string Status, string CreatedAt, string UpdatedAt)
{
    public AddonDto? Addon
    {
        get; init;
    }
}
