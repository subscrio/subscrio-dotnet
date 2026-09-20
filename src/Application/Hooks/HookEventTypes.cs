using Stripe;
using Subscrio.Core.Application.DTOs;

namespace Subscrio.Core.Application.Hooks;

public class CustomerMutationHookEvent
{
    public required string Type { get; init; }
    public required string Phase { get; init; }
    public required string Source { get; init; }
    public required string OccurredAt { get; init; }
    public long? EntityId { get; init; }
    public CustomerDto? Old { get; init; }
    public CustomerDto? New { get; set; }
}

public class SubscriptionMutationHookEvent
{
    public required string Type { get; init; }
    public required string Phase { get; init; }
    public required string Source { get; init; }
    public required string OccurredAt { get; init; }
    public long? EntityId { get; init; }
    public long? CustomerId { get; init; }
    public SubscriptionDto? Old { get; init; }
    public SubscriptionDto? New { get; set; }
    public string? FeatureKey { get; set; }
    public string? Value { get; set; }
    public string? OverrideType { get; set; }
}

public class StripeReceivedHookEvent
{
    public required string Type { get; init; }
    public required string Phase { get; init; }
    public required string OccurredAt { get; init; }
    public required Event Data { get; init; }
    public string? StripeCustomerId { get; init; }
    public string? StripeSubscriptionId { get; init; }
}

public delegate Task CustomerHookHandler(CustomerMutationHookEvent evt, CancellationToken cancellationToken);
public delegate Task SubscriptionHookHandler(SubscriptionMutationHookEvent evt, CancellationToken cancellationToken);
public delegate Task StripeReceivedHookHandler(StripeReceivedHookEvent evt, CancellationToken cancellationToken);
