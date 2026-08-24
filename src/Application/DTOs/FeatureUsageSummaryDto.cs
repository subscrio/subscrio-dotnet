namespace Subscrio.Core.Application.DTOs;

public record FeatureUsageSummaryDto(
    int ActiveSubscriptions,
    List<string> EnabledFeatures,
    List<string> DisabledFeatures,
    Dictionary<string, double> NumericFeatures,
    Dictionary<string, string> TextFeatures
);


