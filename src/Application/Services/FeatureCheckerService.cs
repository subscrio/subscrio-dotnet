using Subscrio.Core.Application.Constants;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Application.Errors;
using Subscrio.Core.Application.Mappers;
using Subscrio.Core.Application.Repositories;
using Subscrio.Core.Domain.Entities;
using Subscrio.Core.Domain.Services;
using Subscrio.Core.Domain.ValueObjects;
using Subscrio.Core.Infrastructure.Database;

namespace Subscrio.Core.Application.Services;

public class FeatureCheckerService
{
    internal FeatureResolutionQuery? ResolutionQuery
    {
        get; set;
    }
    public Task<FeatureValueExplanationDto> ExplainForCustomerAsync(string customerKey, string productKey, string featureKey) => ResolutionQuery!.ExplainAsync(customerKey, productKey, featureKey);
    public async Task<FeatureValueExplanationDto> ExplainForSubscriptionAsync(string subscriptionKey, string featureKey)
    {
        var s = await SubscriptionRepository.FindByKeyAsync(subscriptionKey) ?? throw new NotFoundException("Subscription not found");
        var p = await PlanRepository.FindByIdAsync(s.PlanId) ?? throw new NotFoundException("Plan not found");
        var product = await ProductRepository.FindByIdAsync(p.ProductId) ?? throw new NotFoundException("Product not found");
        var c = await CustomerRepository.FindByIdAsync(s.CustomerId) ?? throw new NotFoundException("Customer not found");
        return await ResolutionQuery!.ExplainAsync(c.Key, product.Key, featureKey, subscriptionKey);
    }
    private readonly FeatureValueResolver _resolver;

    public FeatureCheckerService(
        ISubscriptionRepository subscriptionRepository,
        IPlanRepository planRepository,
        IFeatureRepository featureRepository,
        ICustomerRepository customerRepository,
        IProductRepository productRepository
    )
    {
        SubscriptionRepository = subscriptionRepository;
        PlanRepository = planRepository;
        FeatureRepository = featureRepository;
        CustomerRepository = customerRepository;
        ProductRepository = productRepository;
        _resolver = new FeatureValueResolver();
    }

    private ISubscriptionRepository SubscriptionRepository
    {
        get;
    }
    private IPlanRepository PlanRepository
    {
        get;
    }
    private IFeatureRepository FeatureRepository
    {
        get;
    }
    private ICustomerRepository CustomerRepository
    {
        get;
    }
    private IProductRepository ProductRepository
    {
        get;
    }

    /// <summary>
    /// Get feature value for a specific subscription
    /// </summary>
    /// <param name="subscriptionKey">The subscription's external key</param>
    /// <param name="featureKey">The feature's external key</param>
    /// <param name="defaultValue">Default value if feature not found</param>
    /// <returns>The resolved feature value or default</returns>
    public async Task<T?> GetValueForSubscriptionAsync<T>(
        string subscriptionKey,
        string featureKey,
        T? defaultValue = default
    )
    {
        var subscriptionView = await SubscriptionRepository.FindByKeyAsync(subscriptionKey);
        if (subscriptionView == null)
        {
            return defaultValue ?? default;
        }

        var planRecord = await PlanRepository.FindByIdAsync(subscriptionView.PlanId);
        if (planRecord == null)
        {
            return defaultValue ?? default;
        }

        var featureRecord = await FeatureRepository.FindByKeyAsync(featureKey);
        if (featureRecord == null)
        {
            return defaultValue ?? default;
        }

        var featureValueRecords = await PlanRepository.GetFeatureValuesAsync(planRecord.Id);
        var planFeatureValues = FeatureValueMapper.ToPlanFeatureValues(featureValueRecords);

        var overrideRecords = await SubscriptionRepository.GetFeatureOverridesAsync(subscriptionView.Id);
        var featureOverrides = FeatureValueMapper.ToFeatureOverrides(overrideRecords);

        var feature = FeatureMapper.ToDomain(featureRecord);
        var plan = PlanMapper.ToDomain(planRecord, "", null, planFeatureValues);
        var subscription = SubscriptionMapper.ToDomain(subscriptionView, featureOverrides);

        var value = ResolutionQuery != null ? (await ExplainForSubscriptionAsync(subscriptionKey, featureKey)).EffectiveValue : _resolver.Resolve(feature, plan, subscription);
        return ConvertFeatureValue(value, defaultValue);
    }

    /// <summary>
    /// Check if a feature is enabled for a specific subscription
    /// </summary>
    /// <param name="subscriptionKey">The subscription's external key</param>
    /// <param name="featureKey">The feature's external key</param>
    /// <returns>True if feature is enabled (value is 'true')</returns>
    public async Task<bool> IsEnabledForSubscriptionAsync(
        string subscriptionKey,
        string featureKey
    )
    {
        var value = await GetValueForSubscriptionAsync<string>(subscriptionKey, featureKey);
        return value?.ToLowerInvariant() == "true";
    }

    /// <summary>
    /// Get all feature values for a specific subscription
    /// </summary>
    public async Task<Dictionary<string, string>> GetAllFeaturesForSubscriptionAsync(
        string subscriptionKey
    )
    {
        var subscriptionView = await SubscriptionRepository.FindByKeyAsync(subscriptionKey);
        if (subscriptionView == null)
        {
            throw new NotFoundException($"Subscription with key '{subscriptionKey}' not found");
        }

        var planRecord = await PlanRepository.FindByIdAsync(subscriptionView.PlanId);
        if (planRecord == null)
        {
            return new Dictionary<string, string>();
        }

        var product = await ProductRepository.FindByIdAsync(planRecord.ProductId);
        if (product == null)
        {
            throw new NotFoundException("Product not found for plan");
        }

        var features = await FeatureRepository.FindByProductAsync(product.Id);

        var featureValueRecords = await PlanRepository.GetFeatureValuesAsync(planRecord.Id);
        var planFeatureValues = FeatureValueMapper.ToPlanFeatureValues(featureValueRecords);

        var overrideRecords = await SubscriptionRepository.GetFeatureOverridesAsync(subscriptionView.Id);
        var featureOverrides = FeatureValueMapper.ToFeatureOverrides(overrideRecords);

        var resolved = new Dictionary<string, string>();

        var plan = PlanMapper.ToDomain(planRecord, product.Key, null, planFeatureValues);
        var subscription = SubscriptionMapper.ToDomain(subscriptionView, featureOverrides);

        foreach (var featureRecord in features)
        {
            var feature = FeatureMapper.ToDomain(featureRecord);
            var value = ResolutionQuery != null ? (await ExplainForSubscriptionAsync(subscriptionKey, feature.Key)).EffectiveValue : _resolver.Resolve(feature, plan, subscription);
            resolved[feature.Key] = value;
        }

        return resolved;
    }

    /// <summary>
    /// Get feature value for a customer in a specific product
    /// </summary>
    /// <param name="customerKey">The customer's external key</param>
    /// <param name="productKey">The product's external key</param>
    /// <param name="featureKey">The feature's external key</param>
    /// <param name="defaultValue">Default value if feature not found</param>
    /// <returns>The resolved feature value or default</returns>
    public async Task<T?> GetValueForCustomerAsync<T>(
        string customerKey,
        string productKey,
        string featureKey,
        T? defaultValue = default
    )
    {
        var customer = await CustomerRepository.FindByKeyAsync(customerKey);
        if (customer == null)
        {
            return defaultValue ?? default;
        }

        var product = await ProductRepository.FindByKeyAsync(productKey);
        if (product == null)
        {
            return defaultValue ?? default;
        }

        var feature = await FeatureRepository.FindByKeyAsync(featureKey);
        if (feature == null)
        {
            return defaultValue ?? default;
        }

        if (ResolutionQuery != null)
            return ConvertFeatureValue((await ResolutionQuery.ExplainAsync(customerKey, productKey, featureKey)).EffectiveValue, defaultValue);
        var context = await LoadCustomerProductSubscriptionContextAsync(customer.Id, productKey);
        var featureDomain = FeatureMapper.ToDomain(feature);

        if (context.ProductSubscriptions.Count == 0)
        {
            return ConvertFeatureValue(featureDomain.DefaultValue, defaultValue);
        }

        // Resolve using hierarchy — do not lock onto the first subscription's feature default
        // when another active/trial subscription has a plan value (mirrors FeatureValueResolver.ResolveAll).
        var resolvedValue = featureDomain.DefaultValue;

        foreach (var subscriptionView in context.ProductSubscriptions)
        {
            var planRecord = context.PlanMap[subscriptionView.PlanId];
            var productRecord = context.ProductMap[planRecord.ProductId];

            var overrideRecords = await SubscriptionRepository.GetFeatureOverridesAsync(subscriptionView.Id);
            var featureOverrides = FeatureValueMapper.ToFeatureOverrides(overrideRecords);

            var planFeatureValues = context.PlanFeatureValuesMap.GetValueOrDefault(planRecord.Id, new List<PlanFeatureValue>());
            var plan = PlanMapper.ToDomain(planRecord, productRecord.Key, null, planFeatureValues);
            var subscription = SubscriptionMapper.ToDomain(subscriptionView, featureOverrides);

            var value = _resolver.Resolve(featureDomain, plan, subscription);

            if (featureDomain.Id.HasValue)
            {
                var hasOverride = featureOverrides.Any(o => o.FeatureId == featureDomain.Id.Value);
                if (hasOverride)
                {
                    resolvedValue = value;
                    break;
                }

                var hasPlanValue = planFeatureValues.Any(pf => pf.FeatureId == featureDomain.Id.Value);
                if (hasPlanValue && resolvedValue == featureDomain.DefaultValue)
                {
                    resolvedValue = value;
                }
            }
        }

        return ConvertFeatureValue(resolvedValue, defaultValue);
    }

    /// <summary>
    /// Check if a feature is enabled for a customer in a specific product
    /// </summary>
    public async Task<bool> IsEnabledForCustomerAsync(
        string customerKey,
        string productKey,
        string featureKey
    )
    {
        var value = await GetValueForCustomerAsync<string>(customerKey, productKey, featureKey);
        return value?.ToLowerInvariant() == "true";
    }

    /// <summary>
    /// Get all feature values for a customer in a specific product
    /// </summary>
    public async Task<Dictionary<string, string>> GetAllFeaturesForCustomerAsync(
        string customerKey,
        string productKey
    )
    {
        var customer = await CustomerRepository.FindByKeyAsync(customerKey);
        if (customer == null)
        {
            return new Dictionary<string, string>();
        }

        var product = await ProductRepository.FindByKeyAsync(productKey);
        if (product == null)
        {
            return new Dictionary<string, string>();
        }

        var features = await FeatureRepository.FindByProductAsync(product.Id);
        if (ResolutionQuery != null)
        {
            var result = new Dictionary<string, string>();
            foreach (var f in features)
                result[f.Key] = (await ResolutionQuery.ExplainAsync(customerKey, productKey, f.Key)).EffectiveValue;
            return result;
        }
        var context = await LoadCustomerProductSubscriptionContextAsync(customer.Id, productKey);

        if (context.ProductSubscriptions.Count == 0)
        {
            var resolved = new Dictionary<string, string>();
            foreach (var feature in features)
            {
                resolved[feature.Key] = feature.DefaultValue;
            }

            return resolved;
        }

        var featureDomains = features.Select(FeatureMapper.ToDomain).ToList();
        var planDomains = new Dictionary<long, Plan>();
        foreach (var kvp in context.PlanMap)
        {
            var productRecord = context.ProductMap[kvp.Value.ProductId];
            var planFeatureValues = context.PlanFeatureValuesMap.GetValueOrDefault(kvp.Key, new List<PlanFeatureValue>());
            planDomains[kvp.Key] = PlanMapper.ToDomain(kvp.Value, productRecord.Key, null, planFeatureValues);
        }

        var subscriptionDomains = new List<Subscription>();
        foreach (var subscriptionView in context.ProductSubscriptions)
        {
            var overrideRecords = await SubscriptionRepository.GetFeatureOverridesAsync(subscriptionView.Id);
            var featureOverrides = FeatureValueMapper.ToFeatureOverrides(overrideRecords);

            subscriptionDomains.Add(SubscriptionMapper.ToDomain(subscriptionView, featureOverrides));
        }

        return _resolver.ResolveAll(featureDomains, planDomains, subscriptionDomains);
    }

    /// <summary>
    /// Check if customer has access to a specific plan
    /// </summary>
    public async Task<bool> HasPlanAccessAsync(
        string customerKey,
        string productKey,
        string planKey
    )
    {
        var customer = await CustomerRepository.FindByKeyAsync(customerKey);
        if (customer == null)
        {
            return false;
        }

        var product = await ProductRepository.FindByKeyAsync(productKey);
        if (product == null)
        {
            return false;
        }

        var plan = await PlanRepository.FindByKeyAsync(planKey);
        if (plan == null)
        {
            return false;
        }

        if (plan.ProductId != product.Id)
        {
            return false;
        }

        var subscriptions = await LoadCustomerSubscriptionsAsync(customer.Id);

        return subscriptions.Any(s =>
            s.PlanId == plan.Id &&
            IsActiveOrTrial(s)
        );
    }

    /// <summary>
    /// Get all active plans for a customer
    /// </summary>
    public async Task<List<string>> GetActivePlansAsync(string customerKey)
    {
        var customer = await CustomerRepository.FindByKeyAsync(customerKey);
        if (customer == null)
        {
            return new List<string>();
        }

        var subscriptions = await LoadCustomerSubscriptionsAsync(customer.Id);

        var activeSubscriptions = subscriptions
            .Where(IsActiveOrTrial)
            .ToList();

        var planIds = activeSubscriptions.Select(s => s.PlanId).Distinct().ToList();
        var plans = await PlanRepository.FindByIdsAsync(planIds);

        return plans.Select(plan => plan.Key).Distinct().ToList();
    }

    /// <summary>
    /// Get feature usage summary for a customer in a specific product.
    /// Counts only active/trial subscriptions scoped to the given product.
    /// </summary>
    public async Task<FeatureUsageSummaryDto> GetFeatureUsageSummaryAsync(
        string customerKey,
        string productKey
    )
    {
        var customer = await CustomerRepository.FindByKeyAsync(customerKey);
        var product = await ProductRepository.FindByKeyAsync(productKey);

        var activeSubscriptions = 0;
        if (customer != null && product != null)
        {
            var subscriptions = await LoadCustomerSubscriptionsAsync(customer.Id);

            var planIds = subscriptions.Select(s => s.PlanId).Distinct().ToList();
            var plans = await PlanRepository.FindByIdsAsync(planIds);
            var productPlanIds = plans
                .Where(p => p.ProductId == product.Id)
                .Select(p => p.Id)
                .ToHashSet();

            activeSubscriptions = subscriptions.Count(s =>
                productPlanIds.Contains(s.PlanId) && IsActiveOrTrial(s));
        }

        var allFeatures = await GetAllFeaturesForCustomerAsync(customerKey, productKey);

        var enabledFeatures = new List<string>();
        var disabledFeatures = new List<string>();
        var numericFeatures = new Dictionary<string, double>();
        var meteredFeatures = new Dictionary<string, long>();
        var textFeatures = new Dictionary<string, string>();

        if (product == null)
        {
            return new FeatureUsageSummaryDto(
                activeSubscriptions,
                enabledFeatures,
                disabledFeatures,
                numericFeatures,
                textFeatures
            );
        }

        var features = await FeatureRepository.FindByProductAsync(product.Id);
        var featureTypeMap = features.ToDictionary(f => f.Key, f => Enum.Parse<FeatureValueType>(f.ValueType, ignoreCase: true));

        foreach (var (featureKey, value) in allFeatures)
        {
            if (!featureTypeMap.TryGetValue(featureKey, out var valueType))
            {
                continue;
            }

            switch (valueType)
            {
                case FeatureValueType.Toggle:
                    if (value.ToLowerInvariant() == "true")
                    {
                        enabledFeatures.Add(featureKey);
                    }
                    else
                    {
                        disabledFeatures.Add(featureKey);
                    }

                    break;
                case FeatureValueType.Metered:
                    meteredFeatures[featureKey] = long.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case FeatureValueType.Numeric:
                    if (double.TryParse(value, out var num))
                    {
                        numericFeatures[featureKey] = num;
                    }

                    break;
                case FeatureValueType.Text:
                    textFeatures[featureKey] = value;
                    break;
            }
        }

        return new FeatureUsageSummaryDto(
            activeSubscriptions,
            enabledFeatures,
            disabledFeatures,
            numericFeatures,
            textFeatures
        )
        {
            MeteredFeatures = meteredFeatures
        };
    }

    private async Task<List<SubscriptionStatusViewRecord>> LoadCustomerSubscriptionsAsync(long customerId)
    {
        return await SubscriptionRepository.FindByCustomerIdAsync(
            customerId,
            new SubscriptionFilterDto
            {
                Limit = ApplicationConstants.MaxSubscriptionsPerCustomer,
                Offset = 0
            }
        );
    }

    private static bool IsActiveOrTrial(SubscriptionStatusViewRecord subscription)
    {
        var status = subscription.ComputedStatus.ToLowerInvariant();
        return status == "active" || status == "trial";
    }

    private async Task<CustomerProductSubscriptionContext> LoadCustomerProductSubscriptionContextAsync(
        long customerId,
        string productKey
    )
    {
        var subscriptions = await LoadCustomerSubscriptionsAsync(customerId);

        var planIds = subscriptions.Select(s => s.PlanId).Distinct().ToList();
        var plans = await PlanRepository.FindByIdsAsync(planIds);
        var planMap = plans.ToDictionary(p => p.Id, p => p);

        var planFeatureValuesMap = new Dictionary<long, List<PlanFeatureValue>>();
        foreach (var planId in planIds)
        {
            var featureValueRecords = await PlanRepository.GetFeatureValuesAsync(planId);
            planFeatureValuesMap[planId] = FeatureValueMapper.ToPlanFeatureValues(featureValueRecords);
        }

        var productIds = plans.Select(p => p.ProductId).Distinct().ToList();
        var products = await ProductRepository.FindByIdsAsync(productIds);
        var productMap = products.ToDictionary(p => p.Id, p => p);

        var productSubscriptions = FilterProductActiveSubscriptions(
            subscriptions,
            planMap,
            productMap,
            productKey
        );

        return new CustomerProductSubscriptionContext(
            productSubscriptions,
            planMap,
            productMap,
            planFeatureValuesMap
        );
    }

    private static List<SubscriptionStatusViewRecord> FilterProductActiveSubscriptions(
        IEnumerable<SubscriptionStatusViewRecord> subscriptions,
        Dictionary<long, PlanRecord> planMap,
        Dictionary<long, ProductRecord> productMap,
        string productKey
    )
    {
        return subscriptions.Where(subscription =>
        {
            if (!planMap.TryGetValue(subscription.PlanId, out var plan))
            {
                return false;
            }

            if (!productMap.TryGetValue(plan.ProductId, out var planProduct))
            {
                return false;
            }

            return planProduct.Key == productKey && IsActiveOrTrial(subscription);
        }).ToList();
    }

    private sealed record CustomerProductSubscriptionContext(
        List<SubscriptionStatusViewRecord> ProductSubscriptions,
        Dictionary<long, PlanRecord> PlanMap,
        Dictionary<long, ProductRecord> ProductMap,
        Dictionary<long, List<PlanFeatureValue>> PlanFeatureValuesMap
    );

    /// <summary>
    /// Convert a stored string feature value to <typeparamref name="T"/>.
    /// Supports string, bool, numeric primitives, decimal, and Guid. Other types fall back to <paramref name="defaultValue"/>.
    /// </summary>
    private static T? ConvertFeatureValue<T>(string? value, T? defaultValue)
    {
        if (value == null)
        {
            return defaultValue ?? default;
        }

        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

        if (targetType == typeof(string))
        {
            return (T)(object)value;
        }

        if (targetType == typeof(bool))
        {
            if (bool.TryParse(value, out var b))
            {
                return (T)(object)b;
            }
            if (value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("on", StringComparison.OrdinalIgnoreCase))
            {
                return (T)(object)true;
            }
            if (value.Equals("0", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("no", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                return (T)(object)false;
            }
            return defaultValue ?? default;
        }

        if (targetType == typeof(Guid))
        {
            return Guid.TryParse(value, out var g) ? (T)(object)g : (defaultValue ?? default);
        }

        try
        {
            if (targetType == typeof(int) ||
                targetType == typeof(long) ||
                targetType == typeof(short) ||
                targetType == typeof(byte) ||
                targetType == typeof(float) ||
                targetType == typeof(double) ||
                targetType == typeof(decimal))
            {
                return (T)Convert.ChangeType(value, targetType, System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        catch
        {
            return defaultValue ?? default;
        }

        // Unsupported T — callers should prefer string or a supported primitive
        return defaultValue ?? default;
    }
}
