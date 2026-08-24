namespace Subscrio.Core.Application.Hooks;

public static class HookEvents
{
    public const string CustomerCreatedBefore = "customer.created.before";
    public const string CustomerCreatedAfter = "customer.created.after";
    public const string CustomerUpdatedBefore = "customer.updated.before";
    public const string CustomerUpdatedAfter = "customer.updated.after";
    public const string CustomerArchivedBefore = "customer.archived.before";
    public const string CustomerArchivedAfter = "customer.archived.after";
    public const string CustomerUnarchivedBefore = "customer.unarchived.before";
    public const string CustomerUnarchivedAfter = "customer.unarchived.after";
    public const string CustomerDeletedBefore = "customer.deleted.before";
    public const string CustomerDeletedAfter = "customer.deleted.after";
    public const string SubscriptionCreatedBefore = "subscription.created.before";
    public const string SubscriptionCreatedAfter = "subscription.created.after";
    public const string SubscriptionUpdatedBefore = "subscription.updated.before";
    public const string SubscriptionUpdatedAfter = "subscription.updated.after";
    public const string SubscriptionArchivedBefore = "subscription.archived.before";
    public const string SubscriptionArchivedAfter = "subscription.archived.after";
    public const string SubscriptionUnarchivedBefore = "subscription.unarchived.before";
    public const string SubscriptionUnarchivedAfter = "subscription.unarchived.after";
    public const string SubscriptionDeletedBefore = "subscription.deleted.before";
    public const string SubscriptionDeletedAfter = "subscription.deleted.after";
    public const string SubscriptionFeatureOverrideAddedBefore = "subscription.featureOverrideAdded.before";
    public const string SubscriptionFeatureOverrideAddedAfter = "subscription.featureOverrideAdded.after";
    public const string SubscriptionFeatureOverrideRemovedBefore = "subscription.featureOverrideRemoved.before";
    public const string SubscriptionFeatureOverrideRemovedAfter = "subscription.featureOverrideRemoved.after";
    public const string SubscriptionTemporaryOverridesClearedBefore = "subscription.temporaryOverridesCleared.before";
    public const string SubscriptionTemporaryOverridesClearedAfter = "subscription.temporaryOverridesCleared.after";
    public const string StripeReceivedBefore = "stripe.received.before";
    public const string StripeReceivedAfter = "stripe.received.after";
}

public enum HookSource
{
    Api,
    Stripe,
    System
}

public enum HookPhase
{
    Before,
    After
}

public static class HookSourceExtensions
{
    public static string ToWireValue(this HookSource source) => source switch
    {
        HookSource.Api => "api",
        HookSource.Stripe => "stripe",
        HookSource.System => "system",
        _ => "api"
    };
}

public static class HookPhaseExtensions
{
    public static string ToWireValue(this HookPhase phase) => phase switch
    {
        HookPhase.Before => "before",
        HookPhase.After => "after",
        _ => "before"
    };
}
