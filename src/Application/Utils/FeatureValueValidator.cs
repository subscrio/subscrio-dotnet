using Subscrio.Core.Application.Errors;
using Subscrio.Core.Domain.ValueObjects;

namespace Subscrio.Core.Application.Utils;

/// <summary>
/// Shared utility for validating feature values based on their type
/// </summary>
public static class FeatureValueValidator
{
    /// <summary>
    /// Validate a feature value against its type
    /// </summary>
    /// <param name="value">The value to validate</param>
    /// <param name="valueType">The type of the feature</param>
    /// <exception cref="ValidationException">If the value is invalid for the type</exception>
    public static void Validate(string value, FeatureValueType valueType)
    {
        switch (valueType)
        {
            case FeatureValueType.Toggle:
                if (!value.Equals("true", StringComparison.OrdinalIgnoreCase) && 
                    !value.Equals("false", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ValidationException("Toggle features must have value \"true\" or \"false\"");
                }
                break;
            case FeatureValueType.Numeric:
                if (!double.TryParse(value, out var num) || !double.IsFinite(num))
                {
                    throw new ValidationException("Numeric features must have a valid number value");
                }
                break;
            case FeatureValueType.Text:
                // Text features accept any string value
                break;
            default:
                throw new ValidationException($"Unknown feature value type: {valueType}");
        }
    }
}

