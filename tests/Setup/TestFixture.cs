using Subscrio.Core;
using Subscrio.Core.Application.DTOs;

namespace Subscrio.Core.Tests.Setup;

/// <summary>
/// Test fixtures helper to create test data quickly
/// </summary>
public class TestFixtures
{
    private readonly Subscrio _subscrio;

    public TestFixtures(Subscrio subscrio)
    {
        _subscrio = subscrio;
    }

    public async Task<ProductDto> CreateProductAsync(Dictionary<string, object>? overrides = null)
    {
        var key = overrides?.ContainsKey("Key") == true 
            ? overrides["Key"].ToString()! 
            : $"product-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

        var dto = new CreateProductDto(
            Key: key,
            DisplayName: overrides?.ContainsKey("DisplayName") == true 
                ? overrides["DisplayName"].ToString()! 
                : "Test Product",
            Description: overrides?.ContainsKey("Description") == true 
                ? overrides["Description"].ToString() 
                : null,
            Metadata: overrides?.ContainsKey("Metadata") == true 
                ? overrides["Metadata"] as Dictionary<string, object?> 
                : null
        );

        return await _subscrio.Products.CreateProductAsync(dto);
    }

    public async Task<FeatureDto> CreateFeatureAsync(Dictionary<string, object>? overrides = null)
    {
        var key = overrides?.ContainsKey("Key") == true 
            ? overrides["Key"].ToString()! 
            : $"feature-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

        var dto = new CreateFeatureDto(
            Key: key,
            DisplayName: overrides?.ContainsKey("DisplayName") == true 
                ? overrides["DisplayName"].ToString()! 
                : "Test Feature",
            ValueType: overrides?.ContainsKey("ValueType") == true 
                ? overrides["ValueType"].ToString()! 
                : "toggle",
            DefaultValue: overrides?.ContainsKey("DefaultValue") == true 
                ? overrides["DefaultValue"].ToString()! 
                : "false",
            Description: overrides?.ContainsKey("Description") == true 
                ? overrides["Description"].ToString() 
                : null,
            GroupName: overrides?.ContainsKey("GroupName") == true 
                ? overrides["GroupName"].ToString() 
                : null,
            Validator: overrides?.ContainsKey("Validator") == true 
                ? overrides["Validator"] as Dictionary<string, object?> 
                : null,
            Metadata: overrides?.ContainsKey("Metadata") == true 
                ? overrides["Metadata"] as Dictionary<string, object?> 
                : null
        );

        return await _subscrio.Features.CreateFeatureAsync(dto);
    }

    public async Task<CustomerDto> CreateCustomerAsync(Dictionary<string, object>? overrides = null)
    {
        var key = overrides?.ContainsKey("Key") == true 
            ? overrides["Key"].ToString()! 
            : $"customer-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

        var dto = new CreateCustomerDto(
            Key: key,
            DisplayName: overrides?.ContainsKey("DisplayName") == true 
                ? overrides["DisplayName"].ToString() 
                : null,
            Email: overrides?.ContainsKey("Email") == true 
                ? overrides["Email"].ToString() 
                : null,
            ExternalBillingId: overrides?.ContainsKey("ExternalBillingId") == true 
                ? overrides["ExternalBillingId"].ToString() 
                : null,
            Metadata: overrides?.ContainsKey("Metadata") == true 
                ? overrides["Metadata"] as Dictionary<string, object?> 
                : null
        );

        return await _subscrio.Customers.CreateCustomerAsync(dto);
    }

    public async Task<PlanDto> CreatePlanAsync(string productKey, Dictionary<string, object>? overrides = null)
    {
        var key = overrides?.ContainsKey("Key") == true 
            ? overrides["Key"].ToString()! 
            : $"plan-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

        var dto = new CreatePlanDto(
            ProductKey: productKey,
            Key: key,
            DisplayName: overrides?.ContainsKey("DisplayName") == true 
                ? overrides["DisplayName"].ToString()! 
                : "Test Plan",
            Description: overrides?.ContainsKey("Description") == true 
                ? overrides["Description"].ToString() 
                : null,
            OnExpireTransitionToBillingCycleKey: overrides?.ContainsKey("OnExpireTransitionToBillingCycleKey") == true 
                ? overrides["OnExpireTransitionToBillingCycleKey"].ToString() 
                : null,
            Metadata: overrides?.ContainsKey("Metadata") == true 
                ? overrides["Metadata"] as Dictionary<string, object?> 
                : null
        );

        return await _subscrio.Plans.CreatePlanAsync(dto);
    }

    public async Task<BillingCycleDto> CreateBillingCycleAsync(string planKey, Dictionary<string, object>? overrides = null)
    {
        var key = overrides?.ContainsKey("Key") == true 
            ? overrides["Key"].ToString()! 
            : $"billing-cycle-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

        int? durationValue = null;
        if (overrides?.ContainsKey("DurationValue") == true)
        {
            durationValue = Convert.ToInt32(overrides["DurationValue"]);
        }
        else
        {
            durationValue = 1; // Default to 1 for non-forever billing cycles
        }

        var dto = new CreateBillingCycleDto(
            PlanKey: planKey,
            Key: key,
            DisplayName: overrides?.ContainsKey("DisplayName") == true 
                ? overrides["DisplayName"].ToString()! 
                : "Test Billing Cycle",
            DurationUnit: overrides?.ContainsKey("DurationUnit") == true 
                ? overrides["DurationUnit"].ToString()! 
                : "months",
            Description: overrides?.ContainsKey("Description") == true 
                ? overrides["Description"].ToString() 
                : null,
            DurationValue: durationValue,
            ExternalProductId: overrides?.ContainsKey("ExternalProductId") == true 
                ? overrides["ExternalProductId"].ToString() 
                : null
        );

        return await _subscrio.BillingCycles.CreateBillingCycleAsync(dto);
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(
        string customerKey,
        string billingCycleKey,
        Dictionary<string, object>? overrides = null)
    {
        var dto = new CreateSubscriptionDto(
            Key: overrides?.ContainsKey("Key") == true 
                ? overrides["Key"].ToString()! 
                : $"sub-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            CustomerKey: customerKey,
            BillingCycleKey: billingCycleKey,
            ActivationDate: overrides?.ContainsKey("ActivationDate") == true 
                ? (DateTime?)overrides["ActivationDate"] 
                : null,
            ExpirationDate: overrides?.ContainsKey("ExpirationDate") == true 
                ? (DateTime?)overrides["ExpirationDate"] 
                : null,
            TrialEndDate: overrides?.ContainsKey("TrialEndDate") == true 
                ? (DateTime?)overrides["TrialEndDate"] 
                : null,
            CurrentPeriodStart: overrides?.ContainsKey("CurrentPeriodStart") == true 
                ? (DateTime?)overrides["CurrentPeriodStart"] 
                : null,
            CurrentPeriodEnd: overrides?.ContainsKey("CurrentPeriodEnd") == true 
                ? (DateTime?)overrides["CurrentPeriodEnd"] 
                : null,
            StripeSubscriptionId: overrides?.ContainsKey("StripeSubscriptionId") == true 
                ? overrides["StripeSubscriptionId"].ToString() 
                : null,
            Metadata: overrides?.ContainsKey("Metadata") == true 
                ? overrides["Metadata"] as Dictionary<string, object?> 
                : null
        );

        return await _subscrio.Subscriptions.CreateSubscriptionAsync(dto);
    }

    /// <summary>
    /// Sets up a complete product with features and plans
    /// </summary>
    public async Task<CompleteProductSetup> SetupCompleteProductAsync()
    {
        var product = await CreateProductAsync();

        var features = await Task.WhenAll(new[]
        {
            CreateFeatureAsync(new Dictionary<string, object>
            {
                ["Key"] = "max-projects",
                ["ValueType"] = "numeric",
                ["DefaultValue"] = "10"
            }),
            CreateFeatureAsync(new Dictionary<string, object>
            {
                ["Key"] = "gantt-charts",
                ["ValueType"] = "toggle",
                ["DefaultValue"] = "false"
            })
        });

        // Associate features with product
        foreach (var feature in features)
        {
            await _subscrio.Products.AssociateFeatureAsync(product.Key, feature.Key);
        }

        var basicPlan = await CreatePlanAsync(product.Key, new Dictionary<string, object>
        {
            ["Key"] = "basic",
            ["DisplayName"] = "Basic Plan"
        });

        var proPlan = await CreatePlanAsync(product.Key, new Dictionary<string, object>
        {
            ["Key"] = "pro",
            ["DisplayName"] = "Pro Plan"
        });

        // Set feature values
        await _subscrio.Plans.SetFeatureValueAsync(proPlan.Key, features[0].Key, "50");
        await _subscrio.Plans.SetFeatureValueAsync(proPlan.Key, features[1].Key, "true");

        return new CompleteProductSetup
        {
            Product = product,
            Features = features.ToList(),
            Plans = new List<PlanDto> { basicPlan, proPlan }
        };
    }
}

public class CompleteProductSetup
{
    public required ProductDto Product { get; init; }
    public required List<FeatureDto> Features { get; init; }
    public required List<PlanDto> Plans { get; init; }
}

