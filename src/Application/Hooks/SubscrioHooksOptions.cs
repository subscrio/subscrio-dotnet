namespace Subscrio.Core.Application.Hooks;

/// <summary>
/// Optional config-time hook registration.
/// </summary>
public class SubscrioHooksOptions
{
    public CustomerHookHandler? OnCustomerCreatedBefore { get; init; }
    public CustomerHookHandler? OnCustomerCreatedAfter { get; init; }
    public CustomerHookHandler? OnCustomerUpdatedBefore { get; init; }
    public CustomerHookHandler? OnCustomerUpdatedAfter { get; init; }
    public CustomerHookHandler? OnCustomerArchivedBefore { get; init; }
    public CustomerHookHandler? OnCustomerArchivedAfter { get; init; }
    public CustomerHookHandler? OnCustomerUnarchivedBefore { get; init; }
    public CustomerHookHandler? OnCustomerUnarchivedAfter { get; init; }
    public CustomerHookHandler? OnCustomerDeletedBefore { get; init; }
    public CustomerHookHandler? OnCustomerDeletedAfter { get; init; }
    public SubscriptionHookHandler? OnSubscriptionCreatedBefore { get; init; }
    public SubscriptionHookHandler? OnSubscriptionCreatedAfter { get; init; }
    public SubscriptionHookHandler? OnSubscriptionUpdatedBefore { get; init; }
    public SubscriptionHookHandler? OnSubscriptionUpdatedAfter { get; init; }
    public SubscriptionHookHandler? OnSubscriptionArchivedBefore { get; init; }
    public SubscriptionHookHandler? OnSubscriptionArchivedAfter { get; init; }
    public SubscriptionHookHandler? OnSubscriptionUnarchivedBefore { get; init; }
    public SubscriptionHookHandler? OnSubscriptionUnarchivedAfter { get; init; }
    public SubscriptionHookHandler? OnSubscriptionDeletedBefore { get; init; }
    public SubscriptionHookHandler? OnSubscriptionDeletedAfter { get; init; }
    public SubscriptionHookHandler? OnSubscriptionFeatureOverrideAddedBefore { get; init; }
    public SubscriptionHookHandler? OnSubscriptionFeatureOverrideAddedAfter { get; init; }
    public SubscriptionHookHandler? OnSubscriptionFeatureOverrideRemovedBefore { get; init; }
    public SubscriptionHookHandler? OnSubscriptionFeatureOverrideRemovedAfter { get; init; }
    public SubscriptionHookHandler? OnSubscriptionTemporaryOverridesClearedBefore { get; init; }
    public SubscriptionHookHandler? OnSubscriptionTemporaryOverridesClearedAfter { get; init; }
    public StripeReceivedHookHandler? OnStripeReceivedBefore { get; init; }
    public StripeReceivedHookHandler? OnStripeReceivedAfter { get; init; }
}
