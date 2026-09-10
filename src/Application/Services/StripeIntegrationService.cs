using Stripe;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Application.Errors;
using Subscrio.Core.Application.Hooks;
using Subscrio.Core.Application.Mappers;
using Subscrio.Core.Application.Repositories;
using Subscrio.Core.Domain.Entities;
using Subscrio.Core.Domain.ValueObjects;
using Subscrio.Core.Infrastructure.Database;
using Subscrio.Core.Infrastructure.Utils;
using CustomerEntity = Subscrio.Core.Domain.Entities.Customer;
using PlanEntity = Subscrio.Core.Domain.Entities.Plan;
using SubscriptionEntity = Subscrio.Core.Domain.Entities.Subscription;

namespace Subscrio.Core.Application.Services;

public class StripeIntegrationService
{
    private const string SubscrioCustomerKeyMetadataKey1 = "subscrioCustomerKey";
    private const string SubscrioCustomerKeyMetadataKey2 = "subscrio_customer_key";
    private const string SubscrioSubscriptionKeyMetadataKey1 = "subscrioSubscriptionKey";
    private const string SubscrioSubscriptionKeyMetadataKey2 = "subscrio_subscription_key";

    public StripeIntegrationService(
        ISubscriptionRepository subscriptionRepository,
        ICustomerRepository customerRepository,
        IPlanRepository planRepository,
        IBillingCycleRepository billingCycleRepository,
        string? stripeSecretKey = null,
        HookDispatcher? hooks = null,
        IProductRepository? productRepository = null)
    {
        SubscriptionRepository = subscriptionRepository;
        CustomerRepository = customerRepository;
        PlanRepository = planRepository;
        BillingCycleRepository = billingCycleRepository;
        StripeSecretKey = stripeSecretKey;
        _hooks = hooks ?? new HookDispatcher();
        _productRepository = productRepository;
    }

    private readonly HookDispatcher _hooks;
    private readonly IProductRepository? _productRepository;

    private ISubscriptionRepository SubscriptionRepository { get; }
    private ICustomerRepository CustomerRepository { get; }
    private IPlanRepository PlanRepository { get; }
    private IBillingCycleRepository BillingCycleRepository { get; }
    private string? StripeSecretKey { get; }

    /// <summary>
    /// Process a verified Stripe event
    /// NOTE: Signature verification MUST be done by implementor before calling this
    /// </summary>
    public async Task ProcessStripeEventAsync(Event stripeEvent)
    {
        await _hooks.EmitStripeReceivedAsync(
            HookEvents.StripeReceivedBefore,
            HookPhase.Before,
            stripeEvent);

        switch (stripeEvent.Type)
        {
            case EventTypes.CustomerCreated:
            case EventTypes.CustomerUpdated:
                await HandleCustomerUpsertAsync(stripeEvent.Data.Object as Stripe.Customer);
                break;

            case EventTypes.CustomerDeleted:
                await HandleCustomerDeletedAsync(stripeEvent.Data.Object as Stripe.Customer);
                break;

            case EventTypes.CustomerSubscriptionCreated:
                if (stripeEvent.Data.Object is Stripe.Subscription subscriptionCreated)
                {
                    await HandleSubscriptionCreatedAsync(subscriptionCreated);
                }
                break;

            case EventTypes.CustomerSubscriptionUpdated:
                if (stripeEvent.Data.Object is Stripe.Subscription subscriptionUpdated)
                {
                    await HandleSubscriptionUpdatedAsync(subscriptionUpdated);
                }
                break;

            case EventTypes.CustomerSubscriptionDeleted:
                if (stripeEvent.Data.Object is Stripe.Subscription subscriptionDeleted)
                {
                    await HandleSubscriptionDeletedAsync(subscriptionDeleted);
                }
                break;

            case EventTypes.InvoicePaymentSucceeded:
                if (stripeEvent.Data.Object is Stripe.Invoice invoice)
                {
                    await HandlePaymentSucceededAsync(invoice);
                }
                break;

            default:
                // Ignore unhandled event types
                if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development")
                {
                    Console.WriteLine($"Unhandled Stripe event type: {stripeEvent.Type}");
                }
                break;
        }

        await _hooks.EmitStripeReceivedAsync(
            HookEvents.StripeReceivedAfter,
            HookPhase.After,
            stripeEvent);
    }

    private async Task HandleSubscriptionCreatedAsync(Stripe.Subscription stripeSubscription)
    {
        // In Stripe.net, Customer is a string (customer ID)
        var stripeCustomerId = stripeSubscription.CustomerId ?? throw new ValidationException("Stripe subscription must have a customer ID");
        var customer = await ResolveCustomerAsync(stripeCustomerId, stripeSubscription.Metadata);

        var (billingCycle, plan) = await ResolvePlanFromSubscriptionAsync(stripeSubscription);

        // First check if subscription already linked by Stripe ID
        var existingByStripeId = await SubscriptionRepository.FindByStripeIdAsync(stripeSubscription.Id);
        if (existingByStripeId != null)
        {
            // Get tracked record for update
            var existingSubscriptionRecord = await SubscriptionRepository.FindByKeyForUpdateAsync(existingByStripeId.Key);
            if (existingSubscriptionRecord == null)
            {
                throw new NotFoundException($"Subscription with key '{existingByStripeId.Key}' not found for update");
            }
            
            // Load feature overrides
            var featureOverrides = await SubscriptionRepository.GetFeatureOverridesAsync(existingSubscriptionRecord.Id);
            var overrideList = FeatureValueMapper.ToFeatureOverrides(featureOverrides);
            
            // Convert to domain entity
            var existingSubscriptionEntity = SubscriptionMapper.ToDomain(existingSubscriptionRecord, overrideList);

            SubscriptionDto? oldDto =
                _hooks.HasListeners(HookEvents.SubscriptionUpdatedBefore) ||
                _hooks.HasListeners(HookEvents.SubscriptionUpdatedAfter)
                    ? await ToSubscriptionDtoAsync(existingSubscriptionEntity)
                    : null;
            
            ApplyStripeSubscriptionFields(
                existingSubscriptionEntity,
                customer.Id ?? throw new InvalidOperationException("Customer ID is null"),
                plan.Id ?? throw new InvalidOperationException("Plan ID is null"),
                billingCycle.Id ?? throw new InvalidOperationException("BillingCycle ID is null"),
                stripeSubscription,
                billingCycle.Props.ExternalProductId!
            );
            
            ApplySubscriptionEntityToRecord(existingSubscriptionRecord, existingSubscriptionEntity);
            await SaveSubscriptionWithHooksAsync(
                HookEvents.SubscriptionUpdatedBefore,
                HookEvents.SubscriptionUpdatedAfter,
                existingSubscriptionRecord,
                existingSubscriptionEntity,
                oldDto);
            return;
        }

        // Check metadata for existing subscription to link
        var existingByMetadataView = await FindSubscriptionByMetadataAsync(
            stripeSubscription.Metadata,
            customer.Id ?? throw new InvalidOperationException("Customer ID is null")
        );

        if (existingByMetadataView != null)
        {
            // Get tracked record for update
            var subscriptionRecord2 = await SubscriptionRepository.FindByKeyForUpdateAsync(existingByMetadataView.Key);
            if (subscriptionRecord2 == null)
            {
                throw new NotFoundException($"Subscription with key '{existingByMetadataView.Key}' not found for update");
            }
            
            // Load feature overrides
            var featureOverrides2 = await SubscriptionRepository.GetFeatureOverridesAsync(subscriptionRecord2.Id);
            var overrideList2 = FeatureValueMapper.ToFeatureOverrides(featureOverrides2);
            
            // Convert to domain entity
            var subscriptionEntity2 = SubscriptionMapper.ToDomain(subscriptionRecord2, overrideList2);

            SubscriptionDto? oldDto2 =
                _hooks.HasListeners(HookEvents.SubscriptionUpdatedBefore) ||
                _hooks.HasListeners(HookEvents.SubscriptionUpdatedAfter)
                    ? await ToSubscriptionDtoAsync(subscriptionEntity2)
                    : null;
            
            ApplyStripeSubscriptionFields(
                subscriptionEntity2,
                customer.Id ?? throw new InvalidOperationException("Customer ID is null"),
                plan.Id ?? throw new InvalidOperationException("Plan ID is null"),
                billingCycle.Id ?? throw new InvalidOperationException("BillingCycle ID is null"),
                stripeSubscription,
                billingCycle.Props.ExternalProductId!
            );
            
            ApplySubscriptionEntityToRecord(subscriptionRecord2, subscriptionEntity2);
            await SaveSubscriptionWithHooksAsync(
                HookEvents.SubscriptionUpdatedBefore,
                HookEvents.SubscriptionUpdatedAfter,
                subscriptionRecord2,
                subscriptionEntity2,
                oldDto2);
            return;
        }

        // No existing subscription found, create new one
        var subscriptionKey = GetMetadataValue(
            stripeSubscription.Metadata,
            new[] { SubscrioSubscriptionKeyMetadataKey1, SubscrioSubscriptionKeyMetadataKey2 }
        ) ?? GenerateKey("sub");

        // In Stripe.net, Created is DateTime, not Unix timestamp
        var activationDate = stripeSubscription.Created;

        // Get period dates from the matching subscription item
        var periodItem = GetMatchingSubscriptionItem(stripeSubscription, billingCycle.Props.ExternalProductId ?? string.Empty);
        var currentPeriodStart = periodItem?.CurrentPeriodStart ?? DateHelper.Now();
        var currentPeriodEnd = billingCycle.CalculateNextPeriodEnd(currentPeriodStart) ??
            periodItem?.CurrentPeriodEnd;

        // Merge Stripe metadata with schedule ID
        // In Stripe.net, Schedule is string? (schedule ID), not an object
        var metadata = MergeScheduleIntoMetadata(
            stripeSubscription.Metadata,
            stripeSubscription.ScheduleId,
            null
        );

        var subscriptionEntity = new SubscriptionEntity(
            new SubscriptionProps
            {
                Key = subscriptionKey,
                CustomerId = customer.Id ?? throw new InvalidOperationException("Customer ID is null"),
                PlanId = plan.Id ?? throw new InvalidOperationException("Plan ID is null"),
                BillingCycleId = billingCycle.Id ?? throw new InvalidOperationException("BillingCycle ID is null"),
                Status = MapStripeStatus(stripeSubscription.Status),
                IsArchived = false,
                ActivationDate = activationDate,
                ExpirationDate = null,
                CancellationDate = stripeSubscription.CanceledAt,
                TrialEndDate = stripeSubscription.TrialEnd,
                CurrentPeriodStart = currentPeriodStart,
                CurrentPeriodEnd = currentPeriodEnd,
                StripeSubscriptionId = stripeSubscription.Id,
                FeatureOverrides = new List<FeatureOverride>(),
                Metadata = metadata,
                CreatedAt = DateHelper.Now(),
                UpdatedAt = DateHelper.Now()
            }
        );

        // Convert to record and save
        var subscriptionRecord = SubscriptionMapper.ToPersistence(subscriptionEntity);
        await SaveSubscriptionWithHooksAsync(
            HookEvents.SubscriptionCreatedBefore,
            HookEvents.SubscriptionCreatedAfter,
            subscriptionRecord,
            subscriptionEntity,
            null);
    }

    private async Task HandleSubscriptionUpdatedAsync(Stripe.Subscription stripeSubscription)
    {
        var existingView = await SubscriptionRepository.FindByStripeIdAsync(stripeSubscription.Id);
        if (existingView == null)
        {
            // If not found, treat as creation
            await HandleSubscriptionCreatedAsync(stripeSubscription);
            return;
        }

        // Get tracked record for update
        var subscriptionRecord = await SubscriptionRepository.FindByKeyForUpdateAsync(existingView.Key);
        if (subscriptionRecord == null)
        {
            throw new NotFoundException($"Subscription with key '{existingView.Key}' not found for update");
        }
        
        // Load feature overrides
        var featureOverrides = await SubscriptionRepository.GetFeatureOverridesAsync(subscriptionRecord.Id);
        var overrideList = FeatureValueMapper.ToFeatureOverrides(featureOverrides);
        
        // Convert to domain entity
        var subscriptionEntity = SubscriptionMapper.ToDomain(subscriptionRecord, overrideList);

        SubscriptionDto? oldDto =
            _hooks.HasListeners(HookEvents.SubscriptionUpdatedBefore) ||
            _hooks.HasListeners(HookEvents.SubscriptionUpdatedAfter)
                ? await ToSubscriptionDtoAsync(subscriptionEntity)
                : null;

        var stripeCustomerId = stripeSubscription.CustomerId ?? throw new ValidationException("Stripe subscription must have a customer ID");
        var customer = await ResolveCustomerAsync(stripeCustomerId, stripeSubscription.Metadata);

        var (billingCycle, plan) = await ResolvePlanFromSubscriptionAsync(stripeSubscription);
        ApplyStripeSubscriptionFields(
            subscriptionEntity,
            customer.Id ?? throw new InvalidOperationException("Customer ID is null"),
            plan.Id ?? throw new InvalidOperationException("Plan ID is null"),
            billingCycle.Id ?? throw new InvalidOperationException("BillingCycle ID is null"),
            stripeSubscription,
            billingCycle.Props.ExternalProductId!
        );
        
        ApplySubscriptionEntityToRecord(subscriptionRecord, subscriptionEntity);
        await SaveSubscriptionWithHooksAsync(
            HookEvents.SubscriptionUpdatedBefore,
            HookEvents.SubscriptionUpdatedAfter,
            subscriptionRecord,
            subscriptionEntity,
            oldDto);
    }

    private async Task HandleSubscriptionDeletedAsync(Stripe.Subscription stripeSubscription)
    {
        var subscriptionView = await SubscriptionRepository.FindByStripeIdAsync(stripeSubscription.Id);
        if (subscriptionView == null)
        {
            return; // Already deleted or never existed
        }

        // Get tracked record for update
        var subscriptionRecord = await SubscriptionRepository.FindByKeyForUpdateAsync(subscriptionView.Key);
        if (subscriptionRecord == null)
        {
            return; // Already deleted
        }
        
        // Load feature overrides
        var featureOverrides = await SubscriptionRepository.GetFeatureOverridesAsync(subscriptionRecord.Id);
        var overrideList = FeatureValueMapper.ToFeatureOverrides(featureOverrides);
        
        // Convert to domain entity
        var subscriptionEntity = SubscriptionMapper.ToDomain(subscriptionRecord, overrideList);

        SubscriptionDto? oldDto =
            _hooks.HasListeners(HookEvents.SubscriptionUpdatedBefore) ||
            _hooks.HasListeners(HookEvents.SubscriptionUpdatedAfter)
                ? await ToSubscriptionDtoAsync(subscriptionEntity)
                : null;

        subscriptionEntity.Expire();
        
        ApplySubscriptionEntityToRecord(subscriptionRecord, subscriptionEntity);
        await SaveSubscriptionWithHooksAsync(
            HookEvents.SubscriptionUpdatedBefore,
            HookEvents.SubscriptionUpdatedAfter,
            subscriptionRecord,
            subscriptionEntity,
            oldDto);
    }

    private async Task HandlePaymentSucceededAsync(Stripe.Invoice stripeInvoice)
    {
        var stripeSubscriptionId = GetInvoiceSubscriptionId(stripeInvoice);
        if (string.IsNullOrEmpty(stripeSubscriptionId))
        {
            return; // Not a subscription invoice
        }

        var subscriptionView = await SubscriptionRepository.FindByStripeIdAsync(stripeSubscriptionId);
        if (subscriptionView == null)
        {
            return; // Subscription not found
        }

        // Get tracked record for update
        var subscriptionRecord = await SubscriptionRepository.FindByKeyForUpdateAsync(subscriptionView.Key);
        if (subscriptionRecord == null)
        {
            return; // Subscription not found for update
        }
        
        // Load feature overrides
        var featureOverrides = await SubscriptionRepository.GetFeatureOverridesAsync(subscriptionRecord.Id);
        var overrideList = FeatureValueMapper.ToFeatureOverrides(featureOverrides);
        
        // Convert to domain entity
        var subscriptionEntity = SubscriptionMapper.ToDomain(subscriptionRecord, overrideList);

        SubscriptionDto? oldDto =
            _hooks.HasListeners(HookEvents.SubscriptionUpdatedBefore) ||
            _hooks.HasListeners(HookEvents.SubscriptionUpdatedAfter)
                ? await ToSubscriptionDtoAsync(subscriptionEntity)
                : null;

        // Get the subscription's billing cycle to find matching line item
        var billingCycleRecord = await BillingCycleRepository.FindByIdAsync(subscriptionEntity.Props.BillingCycleId);
        if (billingCycleRecord == null)
        {
            return; // Can't match without billing cycle
        }
        
        // Convert to domain entity to access Props
        var billingCycle = BillingCycleMapper.ToDomain(billingCycleRecord);
        if (string.IsNullOrEmpty(billingCycle.Props.ExternalProductId))
        {
            return; // Can't match without externalProductId
        }

        // Find the invoice line item that matches this billing cycle's Stripe price ID
        var matchingLineItem = stripeInvoice.Lines?.Data?.FirstOrDefault(
            line => GetInvoiceLinePriceId(line) == billingCycle.Props.ExternalProductId
        );

        // In Stripe.net, Period.Start and Period.End are DateTime
        var period = matchingLineItem?.Period ?? stripeInvoice.Lines?.Data?.FirstOrDefault()?.Period;
        if (period != null)
        {
            subscriptionEntity.Props.CurrentPeriodStart = period.Start;
            // In Stripe.net, Period.End is DateTime (non-nullable)
            subscriptionEntity.Props.CurrentPeriodEnd = period.End;
        }

        subscriptionEntity.Props.UpdatedAt = DateHelper.Now();
        
        ApplySubscriptionEntityToRecord(subscriptionRecord, subscriptionEntity);
        await SaveSubscriptionWithHooksAsync(
            HookEvents.SubscriptionUpdatedBefore,
            HookEvents.SubscriptionUpdatedAfter,
            subscriptionRecord,
            subscriptionEntity,
            oldDto);
    }

    /// <summary>
    /// Find billing cycle by Stripe price ID (stored in externalProductId)
    /// </summary>
    private async Task<BillingCycle?> FindBillingCycleByStripePriceIdAsync(string stripePriceId)
    {
        var allCycles = await BillingCycleRepository.FindAllAsync();
        foreach (var cycleRecord in allCycles)
        {
            var cycle = BillingCycleMapper.ToDomain(cycleRecord);
            if (cycle.Props.ExternalProductId == stripePriceId)
            {
                return cycle;
            }
        }
        return null;
    }

    /// <summary>
    /// Obsolete: creating Stripe subscriptions directly from Subscrio is not supported.
    /// Use <see cref="CreateCheckoutSessionAsync"/> to start Checkout, or
    /// <see cref="ProcessStripeEventAsync"/> to sync webhook events.
    /// </summary>
    [Obsolete("Use ProcessStripeEventAsync or CreateCheckoutSessionAsync instead.")]
    public Task<SubscriptionEntity> CreateStripeSubscriptionAsync(
        string customerKey,
        string planKey,
        string billingCycleKey,
        string stripePriceId)
    {
        throw new NotSupportedException(
            "CreateStripeSubscriptionAsync is not supported. " +
            "Use CreateCheckoutSessionAsync to start a Stripe Checkout session, " +
            "or ProcessStripeEventAsync to sync verified Stripe webhook events into Subscrio.");
    }

    private async Task<CustomerEntity> ResolveCustomerAsync(
        string stripeCustomerId,
        Dictionary<string, string>? metadata)
    {
        var existing = await CustomerRepository.FindByExternalBillingIdAsync(stripeCustomerId);
        if (existing != null)
        {
            // Convert CustomerRecord to domain entity
            return CustomerMapper.ToDomain(existing);
        }

        var customerKey = GetMetadataValue(
            metadata,
            new[] { SubscrioCustomerKeyMetadataKey1, SubscrioCustomerKeyMetadataKey2 }
        );
        if (customerKey == null)
        {
            throw new NotFoundException(
                $"Customer not found for Stripe customer ID '{stripeCustomerId}'. " +
                $"Provide 'subscrioCustomerKey' metadata when creating Stripe customers or subscriptions."
            );
        }

        var fallbackCustomerRecord = await CustomerRepository.FindByKeyForUpdateAsync(customerKey);
        if (fallbackCustomerRecord == null)
        {
            throw new NotFoundException(
                $"Customer with key '{customerKey}' not found while handling Stripe customer '{stripeCustomerId}'."
            );
        }

        // Convert to domain entity, set external billing ID, mutate tracked record and save
        var fallbackCustomer = CustomerMapper.ToDomain(fallbackCustomerRecord);
        fallbackCustomer.SetExternalBillingId(stripeCustomerId);
        var savedRecord = await SaveCustomerWithHooksAsync(fallbackCustomerRecord, fallbackCustomer);
        
        // Convert back to domain entity for return
        return CustomerMapper.ToDomain(savedRecord);
    }

    private string? GetMetadataValue(Dictionary<string, string>? metadata, string[] keys)
    {
        if (metadata == null)
        {
            return null;
        }

        foreach (var key in keys)
        {
            if (metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static string? GetInvoiceSubscriptionId(Invoice stripeInvoice)
    {
        var subscriptionId = stripeInvoice.Parent?.SubscriptionDetails?.SubscriptionId;
        if (!string.IsNullOrEmpty(subscriptionId))
        {
            return subscriptionId;
        }

        return stripeInvoice.Parent?.SubscriptionDetails?.Subscription?.Id;
    }

    private static string? GetInvoiceLinePriceId(InvoiceLineItem line)
    {
        var priceId = line.Pricing?.PriceDetails?.PriceId;
        if (!string.IsNullOrEmpty(priceId))
        {
            return priceId;
        }

        return line.Pricing?.PriceDetails?.Price?.Id;
    }

    private static SubscriptionItem? GetMatchingSubscriptionItem(
        Stripe.Subscription stripeSubscription,
        string billingCycleExternalProductId)
    {
        var items = stripeSubscription.Items?.Data;
        if (items == null || items.Count == 0)
        {
            return null;
        }

        return items.FirstOrDefault(item => item.Price?.Id == billingCycleExternalProductId)
            ?? items.FirstOrDefault();
    }

    private async Task<(BillingCycle BillingCycle, PlanEntity Plan)> ResolvePlanFromSubscriptionAsync(Stripe.Subscription stripeSubscription)
    {
        var firstItem = stripeSubscription.Items?.Data?.FirstOrDefault();
        if (firstItem?.Price?.Id == null)
        {
            throw new ValidationException("Stripe subscription payload is missing price information");
        }

        var stripePriceId = firstItem.Price.Id;
        var billingCycle = await FindBillingCycleByStripePriceIdAsync(stripePriceId);
        if (billingCycle == null)
        {
            throw new NotFoundException(
                $"Billing cycle not found for Stripe price ID '{stripePriceId}'. " +
                $"Create a billing cycle with externalProductId='{stripePriceId}' to complete the mapping."
            );
        }

        var planRecord = await PlanRepository.FindByIdAsync(billingCycle.Props.PlanId);
        if (planRecord == null)
        {
            throw new NotFoundException($"Plan not found for billing cycle '{billingCycle.Key}'");
        }
        
        // Load plan feature values
        var featureValueRecords = await PlanRepository.GetFeatureValuesAsync(planRecord.Id);
        var featureValues = FeatureValueMapper.ToPlanFeatureValues(featureValueRecords);
        
        // Convert to domain entity
        var plan = PlanMapper.ToDomain(planRecord, "", null, featureValues);

        return (billingCycle, plan);
    }

    private void ApplyStripeSubscriptionFields(
        SubscriptionEntity subscription,
        long customerId,
        long planId,
        long billingCycleId,
        Stripe.Subscription stripeSubscription,
        string billingCycleExternalProductId)
    {
        subscription.Props.CustomerId = customerId;
        subscription.Props.PlanId = planId;
        subscription.Props.BillingCycleId = billingCycleId;
        subscription.Props.ActivationDate = subscription.Props.ActivationDate ?? stripeSubscription.Created;

        var periodItem = GetMatchingSubscriptionItem(stripeSubscription, billingCycleExternalProductId);
        subscription.Props.CurrentPeriodStart = periodItem?.CurrentPeriodStart ?? DateHelper.Now();
        subscription.Props.CurrentPeriodEnd = periodItem?.CurrentPeriodEnd;
        subscription.Props.TrialEndDate = stripeSubscription.TrialEnd;
        subscription.Props.CancellationDate = stripeSubscription.CanceledAt ??
            (stripeSubscription.CancelAtPeriodEnd ? subscription.Props.CancellationDate : null);
        subscription.Props.StripeSubscriptionId = stripeSubscription.Id;
        subscription.Props.Metadata = MergeScheduleIntoMetadata(
            stripeSubscription.Metadata,
            stripeSubscription.ScheduleId,
            subscription.Props.Metadata
        );
        subscription.Props.Status = MapStripeStatus(stripeSubscription.Status);
        subscription.Props.UpdatedAt = DateHelper.Now();
    }

    private SubscriptionStatus MapStripeStatus(string status)
    {
        return status switch
        {
            "active" => SubscriptionStatus.Active,
            "trialing" => SubscriptionStatus.Trial,
            "canceled" => SubscriptionStatus.Cancelled,
            "past_due" or "unpaid" => SubscriptionStatus.CancellationPending,
            "incomplete" => SubscriptionStatus.Pending,
            "incomplete_expired" => SubscriptionStatus.Expired,
            "paused" => SubscriptionStatus.Pending,
            _ => throw new ValidationException($"Unsupported Stripe subscription status '{status}'")
        };
    }

    /// <summary>
    /// Merge Stripe schedule ID into subscription metadata
    /// </summary>
    private Dictionary<string, object?>? MergeScheduleIntoMetadata(
        Dictionary<string, string>? stripeMetadata,
        string? scheduleId,
        Dictionary<string, object?>? existingMetadata)
    {
        var merged = existingMetadata != null
            ? new Dictionary<string, object?>(existingMetadata)
            : new Dictionary<string, object?>();

        // Overlay Stripe metadata on top
        if (stripeMetadata != null)
        {
            foreach (var (key, value) in stripeMetadata)
            {
                merged[key] = value;
            }
        }

        // Add or remove schedule ID from metadata
        if (!string.IsNullOrEmpty(scheduleId))
        {
            merged["stripeScheduleId"] = scheduleId;
        }
        else
        {
            merged.Remove("stripeScheduleId");
        }

        return merged.Count > 0 ? merged : null;
    }

    private async Task HandleCustomerUpsertAsync(Stripe.Customer? stripeCustomer)
    {
        if (stripeCustomer == null)
        {
            return;
        }

        await ResolveCustomerAsync(stripeCustomer.Id, stripeCustomer.Metadata);
    }

    private async Task HandleCustomerDeletedAsync(Stripe.Customer? stripeCustomer)
    {
        if (stripeCustomer == null)
        {
            return;
        }

        var existingLookup = await CustomerRepository.FindByExternalBillingIdAsync(stripeCustomer.Id);
        if (existingLookup == null)
        {
            return;
        }

        var existingRecord = await CustomerRepository.FindByIdForUpdateAsync(existingLookup.Id);
        if (existingRecord == null)
        {
            return;
        }

        // Convert to domain entity, update, mutate tracked record and save
        var existing = CustomerMapper.ToDomain(existingRecord);
        existing.SetExternalBillingId(null);
        await SaveCustomerWithHooksAsync(existingRecord, existing);
    }

    /// <summary>
    /// Find subscription by metadata (subscription key)
    /// Returns SubscriptionStatusViewRecord for read operations
    /// </summary>
    private async Task<SubscriptionStatusViewRecord?> FindSubscriptionByMetadataAsync(
        Dictionary<string, string>? metadata,
        long customerId)
    {
        var subscriptionKey = GetMetadataValue(
            metadata,
            new[] { SubscrioSubscriptionKeyMetadataKey1, SubscrioSubscriptionKeyMetadataKey2 }
        );

        if (subscriptionKey != null)
        {
            var subscriptionView = await SubscriptionRepository.FindByKeyAsync(subscriptionKey);
            if (subscriptionView != null && subscriptionView.CustomerId == customerId)
            {
                return subscriptionView;
            }
        }

        return null;
    }

    /// <summary>
    /// Get Stripe client instance
    /// </summary>
    private StripeClient GetStripeClient(string? secretKey = null)
    {
        var key = secretKey ?? StripeSecretKey;
        if (string.IsNullOrEmpty(key))
        {
            throw new ConfigurationException(
                "Stripe secret key is required. Provide it in config.stripe.secretKey or pass stripeSecretKey parameter."
            );
        }

        return new StripeClient(key);
    }

    /// <summary>
    /// Create a Stripe Checkout Session URL for subscription purchase
    /// </summary>
    public async Task<(string Url, string SessionId)> CreateCheckoutSessionAsync(
        string customerKey,
        string billingCycleKey,
        string successUrl,
        string cancelUrl,
        string? subscriptionKey = null,
        string? stripeSecretKey = null,
        int? quantity = null,
        string? customerEmail = null,
        string? customerName = null,
        bool? allowPromotionCodes = null,
        string? billingAddressCollection = null,
        string[]? paymentMethodTypes = null,
        int? trialPeriodDays = null,
        Dictionary<string, string>? metadata = null)
    {
        var stripe = GetStripeClient(stripeSecretKey);
        var service = new Stripe.Checkout.SessionService(stripe);

        var customer = await CustomerRepository.FindByKeyAsync(customerKey);
        if (customer == null)
        {
            throw new NotFoundException($"Customer with key '{customerKey}' not found");
        }

        var billingCycleRecord = await BillingCycleRepository.FindByKeyAsync(billingCycleKey);
        if (billingCycleRecord == null)
        {
            throw new NotFoundException($"Billing cycle with key '{billingCycleKey}' not found");
        }
        
        // Convert to domain entity to access Props
        var billingCycle = BillingCycleMapper.ToDomain(billingCycleRecord);
        if (string.IsNullOrEmpty(billingCycle.Props.ExternalProductId))
        {
            throw new ValidationException(
                $"Billing cycle '{billingCycleKey}' does not have externalProductId set. " +
                $"Set it to a Stripe price ID to enable checkout."
            );
        }

        if (subscriptionKey != null)
        {
            var existingSubscriptionView = await SubscriptionRepository.FindByKeyAsync(subscriptionKey);
            if (existingSubscriptionView == null)
            {
                throw new NotFoundException($"Subscription with key '{subscriptionKey}' not found");
            }

            if (existingSubscriptionView.CustomerId != customer.Id)
            {
                throw new ConflictException(
                    $"Subscription '{subscriptionKey}' does not belong to customer '{customerKey}'"
                );
            }
        }

        // Ensure Stripe customer exists
        var customerService = new CustomerService(stripe);
        var customerEntity = CustomerMapper.ToDomain(customer);
        var stripeCustomerId = customerEntity.ExternalBillingId;
        if (string.IsNullOrEmpty(stripeCustomerId))
        {
            var createOptions = new CustomerCreateOptions
            {
                Email = customerEmail,
                Name = customerName,
                Metadata = new Dictionary<string, string>
                {
                    { SubscrioCustomerKeyMetadataKey1, customer.Key }
                }
            };
            var stripeCustomer = await customerService.CreateAsync(createOptions);
            stripeCustomerId = stripeCustomer.Id;
            customerEntity.SetExternalBillingId(stripeCustomerId);
            var trackedCustomer = await CustomerRepository.FindByKeyForUpdateAsync(customer.Key)
                ?? throw new NotFoundException($"Customer with key '{customerKey}' not found for update");
            await SaveCustomerWithHooksAsync(trackedCustomer, customerEntity);
        }
        else
        {
            try
            {
                var updateOptions = new CustomerUpdateOptions
                {
                    Email = customerEmail,
                    Name = customerName,
                    Metadata = new Dictionary<string, string>
                    {
                        { SubscrioCustomerKeyMetadataKey1, customer.Key }
                    }
                };
                await customerService.UpdateAsync(stripeCustomerId, updateOptions);
            }
            catch (StripeException ex) when (ex.StripeError?.Code == "resource_missing")
            {
                var createOptions = new CustomerCreateOptions
                {
                    Email = customerEmail,
                    Name = customerName,
                    Metadata = new Dictionary<string, string>
                    {
                        { SubscrioCustomerKeyMetadataKey1, customer.Key }
                    }
                };
                var stripeCustomer = await customerService.CreateAsync(createOptions);
                stripeCustomerId = stripeCustomer.Id;
                customerEntity.SetExternalBillingId(stripeCustomerId);
                var trackedCustomer = await CustomerRepository.FindByKeyForUpdateAsync(customer.Key)
                    ?? throw new NotFoundException($"Customer with key '{customerKey}' not found for update");
                await SaveCustomerWithHooksAsync(trackedCustomer, customerEntity);
            }
        }

        // Build metadata
        var sessionMetadata = new Dictionary<string, string>
        {
            { SubscrioCustomerKeyMetadataKey1, customer.Key }
        };
        if (subscriptionKey != null)
        {
            sessionMetadata[SubscrioSubscriptionKeyMetadataKey1] = subscriptionKey;
        }

        if (metadata != null)
        {
            foreach (var (key, value) in metadata)
            {
                sessionMetadata[key] = value;
            }
        }

        // Build line items - Stripe.net uses different structure
        var lineItems = new List<Stripe.Checkout.SessionLineItemOptions>
        {
            new Stripe.Checkout.SessionLineItemOptions
            {
                Price = billingCycle.Props.ExternalProductId,
                Quantity = quantity ?? 1
            }
        };

        var subscriptionMetadata = new Dictionary<string, string>
        {
            { SubscrioCustomerKeyMetadataKey1, customer.Key }
        };
        if (subscriptionKey != null)
        {
            subscriptionMetadata[SubscrioSubscriptionKeyMetadataKey1] = subscriptionKey;
        }

        if (metadata != null)
        {
            foreach (var (key, value) in metadata)
            {
                subscriptionMetadata[key] = value;
            }
        }

        // Build checkout session params
        var sessionOptions = new Stripe.Checkout.SessionCreateOptions
        {
            Customer = stripeCustomerId,
            Mode = "subscription",
            LineItems = lineItems,
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            Metadata = sessionMetadata,
            SubscriptionData = new Stripe.Checkout.SessionSubscriptionDataOptions
            {
                Metadata = subscriptionMetadata,
                TrialPeriodDays = trialPeriodDays
            }
        };

        if (allowPromotionCodes.HasValue)
        {
            sessionOptions.AllowPromotionCodes = allowPromotionCodes.Value;
        }

        if (!string.IsNullOrEmpty(billingAddressCollection))
        {
            sessionOptions.BillingAddressCollection = billingAddressCollection;
        }

        if (paymentMethodTypes != null && paymentMethodTypes.Length > 0)
        {
            sessionOptions.PaymentMethodTypes = paymentMethodTypes.ToList();
        }

        var session = await service.CreateAsync(sessionOptions);

        if (string.IsNullOrEmpty(session.Url))
        {
            throw new ValidationException("Stripe checkout session was created but did not return a URL");
        }

        return (session.Url, session.Id);
    }

    /// <summary>
    /// Generate a short, unique key with prefix for external reference
    /// </summary>
    private static string GenerateKey(string prefix)
    {
        var guid = Guid.NewGuid().ToString("N");
        var shortId = guid.Substring(0, 12);
        return $"{prefix}_{shortId}";
    }

    private static void ApplySubscriptionEntityToRecord(SubscriptionRecord record, SubscriptionEntity subscription)
    {
        record.Key = subscription.Key;
        record.CustomerId = subscription.Props.CustomerId;
        record.PlanId = subscription.Props.PlanId;
        record.BillingCycleId = subscription.Props.BillingCycleId;
        record.IsArchived = subscription.Props.IsArchived;
        record.ActivationDate = subscription.Props.ActivationDate;
        record.ExpirationDate = subscription.Props.ExpirationDate;
        record.CancellationDate = subscription.Props.CancellationDate;
        record.TrialEndDate = subscription.Props.TrialEndDate;
        record.CurrentPeriodStart = subscription.Props.CurrentPeriodStart;
        record.CurrentPeriodEnd = subscription.Props.CurrentPeriodEnd;
        record.StripeSubscriptionId = subscription.Props.StripeSubscriptionId;
        record.Metadata = subscription.Props.Metadata;
        record.UpdatedAt = subscription.Props.UpdatedAt;
        record.TransitionedAt = subscription.Props.TransitionedAt;
    }

    private async Task<CustomerRecord> SaveCustomerWithHooksAsync(CustomerRecord existingRecord, CustomerEntity customer)
    {
        var oldDto = CustomerMapper.ToDto(CustomerMapper.ToDomain(existingRecord));
        var newDto = CustomerMapper.ToDto(customer);

        var proposed = await _hooks.EmitCustomerBeforeAsync(
            HookEvents.CustomerUpdatedBefore,
            HookSource.Stripe,
            existingRecord.Id,
            oldDto,
            newDto) ?? newDto;

        // Mutate the tracked EF record — never SaveAsync a detached ToPersistence() instance
        ApplyCustomerDtoMutation.Apply(existingRecord, proposed, allowKeyChange: false);

        var saved = await CustomerRepository.SaveAsync(existingRecord);
        await _hooks.EmitCustomerAfterAsync(
            HookEvents.CustomerUpdatedAfter,
            HookSource.Stripe,
            saved.Id,
            oldDto,
            CustomerMapper.ToDto(CustomerMapper.ToDomain(saved)));
        return saved;
    }

    private async Task<SubscriptionDto> ToSubscriptionDtoAsync(SubscriptionEntity subscription)
    {
        var customer = await CustomerRepository.FindByIdAsync(subscription.Props.CustomerId);
        if (customer == null)
        {
            throw new NotFoundException(
                $"Customer not found for subscription '{subscription.Key}' while emitting hooks.");
        }

        var plan = await PlanRepository.FindByIdAsync(subscription.Props.PlanId);
        if (plan == null)
        {
            throw new NotFoundException(
                $"Plan not found for subscription '{subscription.Key}' while emitting hooks.");
        }

        var cycle = await BillingCycleRepository.FindByIdAsync(subscription.Props.BillingCycleId);
        if (cycle == null)
        {
            throw new NotFoundException(
                $"Billing cycle not found for subscription '{subscription.Key}' while emitting hooks.");
        }

        if (_productRepository == null)
        {
            throw new NotFoundException(
                $"Product repository is required to emit subscription hooks for '{subscription.Key}'.");
        }

        var product = await _productRepository.FindByIdAsync(plan.ProductId);
        if (product == null)
        {
            throw new NotFoundException($"Product not found for plan '{plan.Key}' while emitting hooks.");
        }

        return SubscriptionMapper.ToDto(subscription, customer.Key, product.Key, plan.Key, cycle.Key);
    }

    private async Task<SubscriptionRecord> SaveSubscriptionWithHooksAsync(
        string beforeEvent,
        string afterEvent,
        SubscriptionRecord record,
        SubscriptionEntity subscription,
        SubscriptionDto? oldDto)
    {
        if (_hooks.HasListeners(beforeEvent))
        {
            var newDto = await ToSubscriptionDtoAsync(subscription);
            var before = await _hooks.EmitSubscriptionBeforeAsync(
                beforeEvent,
                HookSource.Stripe,
                record.Id == 0 ? null : record.Id,
                subscription.Props.CustomerId,
                oldDto,
                newDto);
            if (before?.New != null)
            {
                ApplySubscriptionDtoMutation.Apply(
                    record,
                    before.New,
                    allowKeyChange: beforeEvent == HookEvents.SubscriptionCreatedBefore);
            }
        }

        var saved = await SubscriptionRepository.SaveAsync(record);

        if (_hooks.HasListeners(afterEvent))
        {
            var featureOverrides = await SubscriptionRepository.GetFeatureOverridesAsync(saved.Id);
            var overrideList = FeatureValueMapper.ToFeatureOverrides(featureOverrides);
            var savedEntity = SubscriptionMapper.ToDomain(saved, overrideList);
            await _hooks.EmitSubscriptionAfterAsync(
                afterEvent,
                HookSource.Stripe,
                saved.Id,
                saved.CustomerId,
                oldDto,
                await ToSubscriptionDtoAsync(savedEntity));
        }
        return saved;
    }
}
