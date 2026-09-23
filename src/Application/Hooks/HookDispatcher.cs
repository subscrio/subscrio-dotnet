using System.Text.Json;
using Subscrio.Core.Application.DTOs;

namespace Subscrio.Core.Application.Hooks;

/// <summary>
/// Dispatches before/after mutation hooks.
/// </summary>
public class HookDispatcher
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly Dictionary<string, List<object>> _handlers = new();

    public HookDispatcher(SubscrioHooksOptions? options = null)
    {
        if (options == null)
            return;
        Register(options.OnSubscriptionAddonAttachedBefore, HookEvents.SubscriptionAddonAttachedBefore);
        Register(options.OnSubscriptionAddonAttachedAfter, HookEvents.SubscriptionAddonAttachedAfter);
        Register(options.OnSubscriptionAddonDetachedBefore, HookEvents.SubscriptionAddonDetachedBefore);
        Register(options.OnSubscriptionAddonDetachedAfter, HookEvents.SubscriptionAddonDetachedAfter);
        Register(options.OnUsageReportedBefore, HookEvents.UsageReportedBefore);
        Register(options.OnUsageReportedAfter, HookEvents.UsageReportedAfter);
        Register(options.OnCreditConsumedBefore, HookEvents.CreditConsumedBefore);
        Register(options.OnCreditConsumedAfter, HookEvents.CreditConsumedAfter);
        Register(options.OnCreditGrantedBefore, HookEvents.CreditGrantedBefore);
        Register(options.OnCreditGrantedAfter, HookEvents.CreditGrantedAfter);
        Register(options.OnCreditAdjustedBefore, HookEvents.CreditAdjustedBefore);
        Register(options.OnCreditAdjustedAfter, HookEvents.CreditAdjustedAfter);
        Register(options.OnCustomerCreatedBefore, HookEvents.CustomerCreatedBefore);
        Register(options.OnCustomerCreatedAfter, HookEvents.CustomerCreatedAfter);
        Register(options.OnCustomerUpdatedBefore, HookEvents.CustomerUpdatedBefore);
        Register(options.OnCustomerUpdatedAfter, HookEvents.CustomerUpdatedAfter);
        Register(options.OnCustomerArchivedBefore, HookEvents.CustomerArchivedBefore);
        Register(options.OnCustomerArchivedAfter, HookEvents.CustomerArchivedAfter);
        Register(options.OnCustomerUnarchivedBefore, HookEvents.CustomerUnarchivedBefore);
        Register(options.OnCustomerUnarchivedAfter, HookEvents.CustomerUnarchivedAfter);
        Register(options.OnCustomerDeletedBefore, HookEvents.CustomerDeletedBefore);
        Register(options.OnCustomerDeletedAfter, HookEvents.CustomerDeletedAfter);
        Register(options.OnSubscriptionCreatedBefore, HookEvents.SubscriptionCreatedBefore);
        Register(options.OnSubscriptionCreatedAfter, HookEvents.SubscriptionCreatedAfter);
        Register(options.OnSubscriptionUpdatedBefore, HookEvents.SubscriptionUpdatedBefore);
        Register(options.OnSubscriptionUpdatedAfter, HookEvents.SubscriptionUpdatedAfter);
        Register(options.OnSubscriptionArchivedBefore, HookEvents.SubscriptionArchivedBefore);
        Register(options.OnSubscriptionArchivedAfter, HookEvents.SubscriptionArchivedAfter);
        Register(options.OnSubscriptionUnarchivedBefore, HookEvents.SubscriptionUnarchivedBefore);
        Register(options.OnSubscriptionUnarchivedAfter, HookEvents.SubscriptionUnarchivedAfter);
        Register(options.OnSubscriptionDeletedBefore, HookEvents.SubscriptionDeletedBefore);
        Register(options.OnSubscriptionDeletedAfter, HookEvents.SubscriptionDeletedAfter);
        Register(options.OnSubscriptionFeatureOverrideAddedBefore, HookEvents.SubscriptionFeatureOverrideAddedBefore);
        Register(options.OnSubscriptionFeatureOverrideAddedAfter, HookEvents.SubscriptionFeatureOverrideAddedAfter);
        Register(options.OnSubscriptionFeatureOverrideRemovedBefore, HookEvents.SubscriptionFeatureOverrideRemovedBefore);
        Register(options.OnSubscriptionFeatureOverrideRemovedAfter, HookEvents.SubscriptionFeatureOverrideRemovedAfter);
        Register(options.OnSubscriptionTemporaryOverridesClearedBefore, HookEvents.SubscriptionTemporaryOverridesClearedBefore);
        Register(options.OnSubscriptionTemporaryOverridesClearedAfter, HookEvents.SubscriptionTemporaryOverridesClearedAfter);
        Register(options.OnStripeReceivedBefore, HookEvents.StripeReceivedBefore);
        Register(options.OnStripeReceivedAfter, HookEvents.StripeReceivedAfter);
    }

    private void Register(object? handler, string eventName)
    {
        if (handler == null)
            return;
        if (!_handlers.TryGetValue(eventName, out var list))
        {
            list = new List<object>();
            _handlers[eventName] = list;
        }
        list.Add(handler);
    }

    public bool HasListeners(string eventName) =>
        _handlers.TryGetValue(eventName, out var list) && list.Count > 0;

    public Action OnCustomerCreatedBefore(CustomerHookHandler handler) => On(HookEvents.CustomerCreatedBefore, handler);
    public Action OnCustomerCreatedAfter(CustomerHookHandler handler) => On(HookEvents.CustomerCreatedAfter, handler);
    public Action OnCustomerUpdatedBefore(CustomerHookHandler handler) => On(HookEvents.CustomerUpdatedBefore, handler);
    public Action OnCustomerUpdatedAfter(CustomerHookHandler handler) => On(HookEvents.CustomerUpdatedAfter, handler);
    public Action OnCustomerArchivedBefore(CustomerHookHandler handler) => On(HookEvents.CustomerArchivedBefore, handler);
    public Action OnCustomerArchivedAfter(CustomerHookHandler handler) => On(HookEvents.CustomerArchivedAfter, handler);
    public Action OnCustomerUnarchivedBefore(CustomerHookHandler handler) => On(HookEvents.CustomerUnarchivedBefore, handler);
    public Action OnCustomerUnarchivedAfter(CustomerHookHandler handler) => On(HookEvents.CustomerUnarchivedAfter, handler);
    public Action OnCustomerDeletedBefore(CustomerHookHandler handler) => On(HookEvents.CustomerDeletedBefore, handler);
    public Action OnCustomerDeletedAfter(CustomerHookHandler handler) => On(HookEvents.CustomerDeletedAfter, handler);
    public Action OnSubscriptionCreatedBefore(SubscriptionHookHandler handler) => On(HookEvents.SubscriptionCreatedBefore, handler);
    public Action OnSubscriptionCreatedAfter(SubscriptionHookHandler handler) => On(HookEvents.SubscriptionCreatedAfter, handler);
    public Action OnSubscriptionUpdatedBefore(SubscriptionHookHandler handler) => On(HookEvents.SubscriptionUpdatedBefore, handler);
    public Action OnSubscriptionUpdatedAfter(SubscriptionHookHandler handler) => On(HookEvents.SubscriptionUpdatedAfter, handler);
    public Action OnSubscriptionArchivedBefore(SubscriptionHookHandler handler) => On(HookEvents.SubscriptionArchivedBefore, handler);
    public Action OnSubscriptionArchivedAfter(SubscriptionHookHandler handler) => On(HookEvents.SubscriptionArchivedAfter, handler);
    public Action OnSubscriptionUnarchivedBefore(SubscriptionHookHandler handler) => On(HookEvents.SubscriptionUnarchivedBefore, handler);
    public Action OnSubscriptionUnarchivedAfter(SubscriptionHookHandler handler) => On(HookEvents.SubscriptionUnarchivedAfter, handler);
    public Action OnSubscriptionDeletedBefore(SubscriptionHookHandler handler) => On(HookEvents.SubscriptionDeletedBefore, handler);
    public Action OnSubscriptionDeletedAfter(SubscriptionHookHandler handler) => On(HookEvents.SubscriptionDeletedAfter, handler);
    public Action OnSubscriptionFeatureOverrideAddedBefore(SubscriptionHookHandler handler) => On(HookEvents.SubscriptionFeatureOverrideAddedBefore, handler);
    public Action OnSubscriptionFeatureOverrideAddedAfter(SubscriptionHookHandler handler) => On(HookEvents.SubscriptionFeatureOverrideAddedAfter, handler);
    public Action OnSubscriptionFeatureOverrideRemovedBefore(SubscriptionHookHandler handler) => On(HookEvents.SubscriptionFeatureOverrideRemovedBefore, handler);
    public Action OnSubscriptionFeatureOverrideRemovedAfter(SubscriptionHookHandler handler) => On(HookEvents.SubscriptionFeatureOverrideRemovedAfter, handler);
    public Action OnSubscriptionTemporaryOverridesClearedBefore(SubscriptionHookHandler handler) => On(HookEvents.SubscriptionTemporaryOverridesClearedBefore, handler);
    public Action OnSubscriptionTemporaryOverridesClearedAfter(SubscriptionHookHandler handler) => On(HookEvents.SubscriptionTemporaryOverridesClearedAfter, handler);
    public Action OnStripeReceivedBefore(StripeReceivedHookHandler handler) => On(HookEvents.StripeReceivedBefore, handler);
    public Action OnStripeReceivedAfter(StripeReceivedHookHandler handler) => On(HookEvents.StripeReceivedAfter, handler);

    public Action OnSubscriptionAddonAttachedBefore(AccountingHookHandler handler) => On(HookEvents.SubscriptionAddonAttachedBefore, handler);
    public Action OnSubscriptionAddonAttachedAfter(AccountingHookHandler handler) => On(HookEvents.SubscriptionAddonAttachedAfter, handler);
    public Action OnSubscriptionAddonDetachedBefore(AccountingHookHandler handler) => On(HookEvents.SubscriptionAddonDetachedBefore, handler);
    public Action OnSubscriptionAddonDetachedAfter(AccountingHookHandler handler) => On(HookEvents.SubscriptionAddonDetachedAfter, handler);
    public Action OnUsageReportedBefore(AccountingHookHandler handler) => On(HookEvents.UsageReportedBefore, handler);
    public Action OnUsageReportedAfter(AccountingHookHandler handler) => On(HookEvents.UsageReportedAfter, handler);
    public Action OnCreditConsumedBefore(AccountingHookHandler handler) => On(HookEvents.CreditConsumedBefore, handler);
    public Action OnCreditConsumedAfter(AccountingHookHandler handler) => On(HookEvents.CreditConsumedAfter, handler);
    public Action OnCreditGrantedBefore(AccountingHookHandler handler) => On(HookEvents.CreditGrantedBefore, handler);
    public Action OnCreditGrantedAfter(AccountingHookHandler handler) => On(HookEvents.CreditGrantedAfter, handler);
    public Action OnCreditAdjustedBefore(AccountingHookHandler handler) => On(HookEvents.CreditAdjustedBefore, handler);
    public Action OnCreditAdjustedAfter(AccountingHookHandler handler) => On(HookEvents.CreditAdjustedAfter, handler);
    internal async Task EmitAccountingAsync(AccountingMutationHookEvent evt)
    {
        if (!_handlers.TryGetValue(evt.Type, out var handlers))
            return;
        foreach (var handler in handlers.ToArray())
        if (handler is AccountingHookHandler typed)
        {
            var payload = evt.Phase == "before" ? evt : new AccountingMutationHookEvent { Type = evt.Type, Phase = evt.Phase, Source = evt.Source, OccurredAt = evt.OccurredAt, Input = (System.Text.Json.Nodes.JsonObject)evt.Input.DeepClone(), Result = evt.Result };
            await typed(payload, default);
        }
    }
    private Action On(string eventName, object handler)
    {
        if (!_handlers.TryGetValue(eventName, out var list))
        {
            list = new List<object>();
            _handlers[eventName] = list;
        }
        list.Add(handler);
        return () =>
        {
            if (_handlers.TryGetValue(eventName, out var current))
            {
                current.Remove(handler);
                if (current.Count == 0)
                    _handlers.Remove(eventName);
            }
        };
    }

    public static T? CloneJson<T>(T? value)
    {
        if (value == null)
            return default;
        var json = JsonSerializer.Serialize(value, JsonOptions);
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    public async Task<CustomerDto?> EmitCustomerBeforeAsync(
        string eventName,
        HookSource source,
        long? entityId,
        CustomerDto? oldDto,
        CustomerDto? newDto,
        CancellationToken cancellationToken = default)
    {
        if (!HasListeners(eventName))
            return newDto;
        var payload = new CustomerMutationHookEvent
        {
            Type = eventName,
            Phase = HookPhase.Before.ToWireValue(),
            Source = source.ToWireValue(),
            OccurredAt = DateTime.UtcNow.ToString("O"),
            EntityId = entityId,
            Old = CloneJson(oldDto),
            New = CloneJson(newDto)
        };
        foreach (var handler in _handlers[eventName].ToList())
        {
            if (handler is CustomerHookHandler typed)
            {
                await typed(payload, cancellationToken);
            }
        }
        return payload.New;
    }

    public async Task EmitCustomerAfterAsync(
        string eventName,
        HookSource source,
        long? entityId,
        CustomerDto? oldDto,
        CustomerDto? newDto,
        CancellationToken cancellationToken = default)
    {
        if (!HasListeners(eventName))
            return;
        var payload = new CustomerMutationHookEvent
        {
            Type = eventName,
            Phase = HookPhase.After.ToWireValue(),
            Source = source.ToWireValue(),
            OccurredAt = DateTime.UtcNow.ToString("O"),
            EntityId = entityId,
            Old = CloneJson(oldDto),
            New = CloneJson(newDto)
        };
        foreach (var handler in _handlers[eventName].ToList())
        {
            if (handler is CustomerHookHandler typed)
            {
                await typed(payload, cancellationToken);
            }
        }
    }

    public async Task<SubscriptionMutationHookEvent?> EmitSubscriptionBeforeAsync(
        string eventName,
        HookSource source,
        long? entityId,
        long? customerId,
        SubscriptionDto? oldDto,
        SubscriptionDto? newDto,
        string? featureKey = null,
        string? value = null,
        string? overrideType = null,
        CancellationToken cancellationToken = default,
        DateTime? expiresAt = null)
    {
        if (!HasListeners(eventName))
            return null;
        var payload = new SubscriptionMutationHookEvent
        {
            Type = eventName,
            Phase = HookPhase.Before.ToWireValue(),
            Source = source.ToWireValue(),
            OccurredAt = DateTime.UtcNow.ToString("O"),
            EntityId = entityId,
            CustomerId = customerId,
            Old = CloneJson(oldDto),
            New = CloneJson(newDto),
            FeatureKey = featureKey,
            Value = value,
            OverrideType = overrideType,
            ExpiresAt = expiresAt
        };
        foreach (var handler in _handlers[eventName].ToList())
        {
            if (handler is SubscriptionHookHandler typed)
            {
                await typed(payload, cancellationToken);
            }
        }
        return payload;
    }

    public async Task EmitSubscriptionAfterAsync(
        string eventName,
        HookSource source,
        long? entityId,
        long? customerId,
        SubscriptionDto? oldDto,
        SubscriptionDto? newDto,
        string? featureKey = null,
        string? value = null,
        string? overrideType = null,
        CancellationToken cancellationToken = default,
        DateTime? expiresAt = null)
    {
        if (!HasListeners(eventName))
            return;
        var payload = new SubscriptionMutationHookEvent
        {
            Type = eventName,
            Phase = HookPhase.After.ToWireValue(),
            Source = source.ToWireValue(),
            OccurredAt = DateTime.UtcNow.ToString("O"),
            EntityId = entityId,
            CustomerId = customerId,
            Old = CloneJson(oldDto),
            New = CloneJson(newDto),
            FeatureKey = featureKey,
            Value = value,
            OverrideType = overrideType,
            ExpiresAt = expiresAt
        };
        foreach (var handler in _handlers[eventName].ToList())
        {
            if (handler is SubscriptionHookHandler typed)
            {
                await typed(payload, cancellationToken);
            }
        }
    }

    public async Task EmitStripeReceivedAsync(
        string eventName,
        HookPhase phase,
        Stripe.Event stripeEvent,
        CancellationToken cancellationToken = default)
    {
        if (!HasListeners(eventName))
            return;

        // Snapshot so handlers cannot mutate the live event used by ProcessStripeEventAsync
        var snapshot = CloneStripeEvent(stripeEvent);
        var (stripeCustomerId, stripeSubscriptionId) = GetStripeEntityRefs(stripeEvent);

        var payload = new StripeReceivedHookEvent
        {
            Type = eventName,
            Phase = phase.ToWireValue(),
            OccurredAt = DateTime.UtcNow.ToString("O"),
            Data = snapshot,
            StripeCustomerId = stripeCustomerId,
            StripeSubscriptionId = stripeSubscriptionId
        };
        foreach (var handler in _handlers[eventName].ToList())
        {
            if (handler is StripeReceivedHookHandler typed)
            {
                await typed(payload, cancellationToken);
            }
        }
    }

    private static Stripe.Event CloneStripeEvent(Stripe.Event stripeEvent)
    {
        try
        {
            var json = stripeEvent.ToJson();
            return Stripe.Event.FromJson(json) ?? stripeEvent;
        }
        catch
        {
            return stripeEvent;
        }
    }

    private static (string? CustomerId, string? SubscriptionId) GetStripeEntityRefs(
        Stripe.Event stripeEvent)
    {
        var obj = stripeEvent.Data?.Object;
        return obj switch
        {
            Stripe.Subscription subscription =>
                (AsStripeId(subscription.CustomerId) ?? AsStripeId(subscription.Customer?.Id),
                    AsStripeId(subscription.Id)),
            Stripe.Invoice invoice =>
                (AsStripeId(invoice.CustomerId) ?? AsStripeId(invoice.Customer?.Id),
                    AsStripeId(invoice.Parent?.SubscriptionDetails?.SubscriptionId)
                        ?? AsStripeId(invoice.Parent?.SubscriptionDetails?.Subscription?.Id)),
            Stripe.Checkout.Session session =>
                (AsStripeId(session.CustomerId) ?? AsStripeId(session.Customer?.Id),
                    AsStripeId(session.SubscriptionId) ?? AsStripeId(session.Subscription?.Id)),
            Stripe.Customer customer => (AsStripeId(customer.Id), null),
            _ => (null, null)
        };
    }

    private static string? AsStripeId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
