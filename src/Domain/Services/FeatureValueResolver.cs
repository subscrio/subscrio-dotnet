using Subscrio.Core.Domain.Entities;

namespace Subscrio.Core.Domain.Services;

public class FeatureValueResolver
{
    /// <summary>
    /// Resolves a feature value using the hierarchy:
    /// 1. Subscription Override (if exists)
    /// 2. Plan Value (if specified)
    /// 3. Feature Default (fallback)
    /// </summary>
    public string Resolve(Feature feature, Plan? plan, Subscription? subscription)
    {
        // STEP 1: Check subscription override
        if (subscription != null && feature.Id.HasValue)
        {
            var @override = subscription.GetFeatureOverride(feature.Id.Value);
            if (@override != null)
            {
                return @override.Value;
            }
        }

        // STEP 2: Check plan value
        if (plan != null && feature.Id.HasValue)
        {
            var planValue = plan.GetFeatureValue(feature.Id.Value);
            if (planValue != null)
            {
                return planValue;
            }
        }

        // STEP 3: Use feature default
        return feature.DefaultValue;
    }

    /// <summary>
    /// Resolves all features for a customer's subscriptions
    /// </summary>
    public Dictionary<string, string> ResolveAll(
        List<Feature> features,
        Dictionary<long, Plan> plans,
        List<Subscription> subscriptions)
    {
        var resolved = new Dictionary<string, string>();

        foreach (var feature in features)
        {
            var value = feature.DefaultValue;

            // Check all subscriptions (if multiple, highest priority wins)
            foreach (var subscription in subscriptions)
            {
                plans.TryGetValue(subscription.PlanId, out var plan);
                var resolvedValue = Resolve(feature, plan, subscription);

                // If subscription has override, it takes precedence
                if (feature.Id.HasValue && subscription.GetFeatureOverride(feature.Id.Value) != null)
                {
                    value = resolvedValue;
                    break; // Override found, stop checking
                }

                // Otherwise, if plan has value and we don't have one yet
                if (feature.Id.HasValue && plan?.GetFeatureValue(feature.Id.Value) != null && value == feature.DefaultValue)
                {
                    value = resolvedValue;
                }
            }

            resolved.Add(feature.Key, value);
        }

        return resolved;
    }
}

