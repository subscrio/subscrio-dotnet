using Subscrio.Core.Domain.Entities;
using Subscrio.Core.Domain.ValueObjects;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Infrastructure.Database;

namespace Subscrio.Core.Application.Mappers;

public static class FeatureMapper
{
    public static FeatureDto ToDto(Feature feature)
    {
        return new FeatureDto(
            Key: feature.Key,
            DisplayName: feature.DisplayName,
            Description: feature.Props.Description,
            ValueType: feature.ValueType.ToString().ToLowerInvariant(),
            DefaultValue: feature.DefaultValue,
            GroupName: feature.Props.GroupName,
            Status: feature.Status.ToString().ToLowerInvariant(),
            Validator: feature.Props.Validator,
            Metadata: feature.Props.Metadata,
            CreatedAt: feature.Props.CreatedAt.ToUniversalTime().ToString("O"),
            UpdatedAt: feature.Props.UpdatedAt.ToUniversalTime().ToString("O")
        );
    }

    public static Feature ToDomain(FeatureRecord record)
    {
        return new Feature(
            new FeatureProps
            {
                Key = record.Key,
                DisplayName = record.DisplayName,
                Description = record.Description,
                ValueType = Enum.Parse<FeatureValueType>(record.ValueType, ignoreCase: true),
                DefaultValue = record.DefaultValue,
                GroupName = record.GroupName,
                Status = Enum.Parse<FeatureStatus>(record.Status, ignoreCase: true),
                Validator = record.Validator,
                Metadata = record.Metadata,
                CreatedAt = record.CreatedAt,
                UpdatedAt = record.UpdatedAt
            },
            record.Id
        );
    }

    public static FeatureRecord ToPersistence(Feature feature)
    {
        return new FeatureRecord
        {
            Id = feature.Id ?? 0,
            Key = feature.Key,
            DisplayName = feature.DisplayName,
            Description = feature.Props.Description,
            ValueType = feature.ValueType.ToString().ToLowerInvariant(),
            DefaultValue = feature.DefaultValue,
            GroupName = feature.Props.GroupName,
            Status = feature.Status.ToString().ToLowerInvariant(),
            Validator = feature.Props.Validator,
            Metadata = feature.Props.Metadata,
            CreatedAt = feature.Props.CreatedAt,
            UpdatedAt = feature.Props.UpdatedAt
        };
    }
}


