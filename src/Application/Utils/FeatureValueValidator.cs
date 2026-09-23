using Subscrio.Core.Application.Errors;
using Subscrio.Core.Domain.ValueObjects;
using ValidationException = Subscrio.Core.Application.Errors.ValidationException;

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
        if (!TryValidate(value, valueType, out var error))
        {
            throw new ValidationException(error!);
        }
    }

    /// <summary>
    /// Validate a feature value from a string type name (toggle/numeric/text).
    /// </summary>
    public static bool TryValidate(string value, string valueType, out string? error)
    {
        if (!Enum.TryParse<FeatureValueType>(valueType, ignoreCase: true, out var parsed))
        {
            error = $"Unknown feature value type: {valueType}";
            return false;
        }

        return TryValidate(value, parsed, out error);
    }

    public static bool TryValidate(string value, FeatureValueType valueType, out string? error)
    {
        switch (valueType)
        {
            case FeatureValueType.Toggle:
                if (!value.Equals("true", StringComparison.OrdinalIgnoreCase) &&
                    !value.Equals("false", StringComparison.OrdinalIgnoreCase))
                {
                    error = "Toggle features must have value \"true\" or \"false\"";
                    return false;
                }
                break;
            case FeatureValueType.Numeric:
                if (!double.TryParse(value, out var num) || !double.IsFinite(num))
                {
                    error = "Numeric features must have a valid number value";
                    return false;
                }
                break;
            case FeatureValueType.Metered:
                if (!long.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var amount) || amount < 0 || amount > 9007199254740991)
                {
                    error = "Metered values must be nonnegative safe integers";
                    return false;
                }
                break;
            case FeatureValueType.Text:
                break;
            default:
                error = $"Unknown feature value type: {valueType}";
                return false;
        }

        error = null;
        return true;
    }
}

