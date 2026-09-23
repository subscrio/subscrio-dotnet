using Subscrio.Core.Infrastructure.Repositories;
using FluentValidation;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Application.Errors;
using Subscrio.Core.Application.Hooks;
using Subscrio.Core.Application.Mappers;
using Subscrio.Core.Application.Repositories;
using Subscrio.Core.Application.Utils;
using Subscrio.Core.Application.Validators;
using Subscrio.Core.Domain.Entities;
using Subscrio.Core.Domain.ValueObjects;
using Subscrio.Core.Infrastructure.Database;
using Subscrio.Core.Infrastructure.Utils;
using ValidationException = Subscrio.Core.Application.Errors.ValidationException;

namespace Subscrio.Core.Application.Services;

public class SubscriptionManagementService
{
    internal CatalogReader? Catalog
    {
        get; set;
    }
    internal IClock Clock { get; set; } = new SystemClock();
    internal SubscriptionAddonManager? AddonService
    {
        get; set;
    }
    public Task<SubscriptionAddonDto> AttachAddonAsync(string subscriptionKey, string addonKey, int quantity = 1) => AddonService!.AttachAddonAsync(subscriptionKey, addonKey, quantity);
    public Task DetachAddonAsync(string subscriptionKey, string addonKey) => AddonService!.DetachAddonAsync(subscriptionKey, addonKey);
    public Task<List<SubscriptionAddonDto>> GetAddonsAsync(string subscriptionKey, int limit = 50, int offset = 0) => AddonService!.ListSubscriptionAddonsAsync(subscriptionKey, limit, offset);
    private readonly ISubscriptionRepository _subscriptionRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly IPlanRepository _planRepository;
    private readonly IBillingCycleRepository _billingCycleRepository;
    private readonly IFeatureRepository _featureRepository;
    private readonly IProductRepository _productRepository;
    private readonly CreateSubscriptionDtoValidator _createValidator;
    private readonly UpdateSubscriptionDtoValidator _updateValidator;
    private readonly SubscriptionFilterDtoValidator _filterValidator;
    private readonly DetailedSubscriptionFilterDtoValidator _detailedFilterValidator;
    private readonly HookDispatcher _hooks;

    public SubscriptionManagementService(
        ISubscriptionRepository subscriptionRepository,
        ICustomerRepository customerRepository,
        IPlanRepository planRepository,
        IBillingCycleRepository billingCycleRepository,
        IFeatureRepository featureRepository,
        IProductRepository productRepository,
        CreateSubscriptionDtoValidator createValidator,
        UpdateSubscriptionDtoValidator updateValidator,
        SubscriptionFilterDtoValidator filterValidator,
        DetailedSubscriptionFilterDtoValidator detailedFilterValidator,
        HookDispatcher? hooks = null)
    {
        _subscriptionRepository = subscriptionRepository;
        _customerRepository = customerRepository;
        _planRepository = planRepository;
        _billingCycleRepository = billingCycleRepository;
        _featureRepository = featureRepository;
        _productRepository = productRepository;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _filterValidator = filterValidator;
        _detailedFilterValidator = detailedFilterValidator;
        _hooks = hooks ?? new HookDispatcher();
    }

    private async Task<SubscriptionDto> ToDtoAsync(SubscriptionStatusViewRecord viewRecord)
    {
        var keys = await ResolveSubscriptionKeysAsync(viewRecord);
        var overrides = await LoadFeatureOverridesAsync(viewRecord.Id);
        var subscription = SubscriptionMapper.ToDomain(viewRecord, overrides);
        var dto = SubscriptionMapper.ToDto(
            subscription,
            keys.CustomerKey,
            keys.ProductKey,
            keys.PlanKey,
            keys.BillingCycleKey
        );
        foreach (var o in overrides)
        {
            var f = await _featureRepository.FindByIdAsync(o.FeatureId);
            if (f != null)
                dto.FeatureOverrides.Add(new(o.FeatureId, o.Value, o.Type.ToString().ToLowerInvariant(), DatabaseSession.Iso(o.CreatedAt), f.Key, o.ExpiresAt is DateTime expiry ? DatabaseSession.Iso(expiry) : null, o.ExpiresAt == null || o.ExpiresAt > Clock.UtcNow));
        }
        if (Catalog != null)
            dto.Addons = await Catalog.SubscriptionAddonsAsync(dto.Key);
        return dto;
    }

    private async Task<List<FeatureOverride>> LoadFeatureOverridesAsync(long subscriptionId)
    {
        var featureOverrides = await _subscriptionRepository.GetFeatureOverridesAsync(subscriptionId);
        return FeatureValueMapper.ToFeatureOverrides(featureOverrides);
    }

    private async Task<(string CustomerKey, string ProductKey, string PlanKey, string BillingCycleKey)> ResolveSubscriptionKeysAsync(SubscriptionStatusViewRecord subscription)
    {
        // Get customer
        var customer = await _customerRepository.FindByIdAsync(subscription.CustomerId);
        if (customer == null)
        {
            throw new NotFoundException(
                $"Customer not found for subscription '{subscription.Key}'. " +
                "This indicates data integrity issue - subscription references invalid customer."
            );
        }

        // Get plan
        var plan = await _planRepository.FindByIdAsync(subscription.PlanId);
        if (plan == null)
        {
            throw new NotFoundException(
                $"Plan not found for subscription '{subscription.Key}'. " +
                "This indicates data integrity issue - subscription references invalid plan."
            );
        }

        // Get product to resolve ProductKey
        var product = await _productRepository.FindByIdAsync(plan.ProductId);
        if (product == null)
        {
            throw new NotFoundException("Product not found for plan");
        }

        // Get billing cycle (required)
        var cycle = await _billingCycleRepository.FindByIdAsync(subscription.BillingCycleId);
        if (cycle == null)
        {
            throw new NotFoundException(
                $"Billing cycle not found for subscription '{subscription.Key}'. " +
                "This indicates data integrity issue - subscription references invalid billing cycle."
            );
        }

        return (customer.Key, product.Key, plan.Key, cycle.Key);
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(CreateSubscriptionDto dto)
    {
        var validationResult = await _createValidator.ValidateAsync(dto);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(
                "Invalid subscription data",
                validationResult.Errors
            );
        }

        // Verify customer exists
        var customer = await _customerRepository.FindByKeyAsync(dto.CustomerKey);
        if (customer == null)
        {
            throw new NotFoundException($"Customer with key '{dto.CustomerKey}' not found");
        }

        // Get billing cycle and derive plan/product from it
        var billingCycle = await _billingCycleRepository.FindByKeyAsync(dto.BillingCycleKey);
        if (billingCycle == null)
        {
            throw new NotFoundException($"Billing cycle with key '{dto.BillingCycleKey}' not found");
        }

        // Get plan from billing cycle
        var plan = await _planRepository.FindByIdAsync(billingCycle.PlanId);
        if (plan == null)
        {
            throw new NotFoundException($"Plan not found for billing cycle '{dto.BillingCycleKey}'");
        }

        var billingCycleId = billingCycle.Id;

        // Check for duplicate subscription key
        var existingKey = await _subscriptionRepository.FindByKeyAsync(dto.Key);
        if (existingKey != null)
        {
            throw new ConflictException($"Subscription with key '{dto.Key}' already exists");
        }

        // Check for duplicate Stripe subscription ID if provided
        if (dto.StripeSubscriptionId != null)
        {
            var existing = await _subscriptionRepository.FindByStripeIdAsync(dto.StripeSubscriptionId);
            if (existing != null)
            {
                throw new ConflictException($"Subscription with Stripe ID '{dto.StripeSubscriptionId}' already exists");
            }
        }

        var trialEndDate = dto.TrialEndDate;

        // Calculate currentPeriodEnd based on billing cycle duration
        var currentPeriodStart = dto.CurrentPeriodStart ?? DateHelper.Now();
        var billingCycleDomain = BillingCycleMapper.ToDomain(billingCycle);
        var currentPeriodEnd = dto.CurrentPeriodEnd ?? CalculatePeriodEnd(currentPeriodStart, billingCycleDomain);

        // Create record from DTO
        var record = new SubscriptionRecord
        {
            Id = 0, // Will be set by EF Core
            Key = dto.Key,
            CustomerId = customer.Id,
            PlanId = plan.Id,
            BillingCycleId = billingCycleId,
            IsArchived = false,
            ActivationDate = dto.ActivationDate ?? DateHelper.Now(),
            ExpirationDate = dto.ExpirationDate,
            CancellationDate = dto.CancellationDate,
            TrialEndDate = trialEndDate,
            CurrentPeriodStart = currentPeriodStart,
            CurrentPeriodEnd = currentPeriodEnd,
            StripeSubscriptionId = dto.StripeSubscriptionId,
            Metadata = dto.Metadata,
            CreatedAt = DateHelper.Now(),
            UpdatedAt = DateHelper.Now()
        };

        var product = await _productRepository.FindByIdAsync(plan.ProductId)
            ?? throw new NotFoundException($"Product not found for plan '{plan.Key}'");
        var proposedDomain = new Subscription(
            new SubscriptionProps
            {
                Key = record.Key,
                CustomerId = record.CustomerId,
                PlanId = record.PlanId,
                BillingCycleId = record.BillingCycleId,
                Status = SubscriptionStatus.Active,
                IsArchived = false,
                ActivationDate = record.ActivationDate,
                ExpirationDate = record.ExpirationDate,
                CancellationDate = record.CancellationDate,
                TrialEndDate = record.TrialEndDate,
                CurrentPeriodStart = record.CurrentPeriodStart,
                CurrentPeriodEnd = record.CurrentPeriodEnd,
                StripeSubscriptionId = record.StripeSubscriptionId,
                FeatureOverrides = new List<FeatureOverride>(),
                Metadata = record.Metadata,
                CreatedAt = record.CreatedAt,
                UpdatedAt = record.UpdatedAt
            }
        );
        var proposed = SubscriptionMapper.ToDto(
            proposedDomain,
            customer.Key,
            product.Key,
            plan.Key,
            billingCycle.Key
        );
        var before = await _hooks.EmitSubscriptionBeforeAsync(
            HookEvents.SubscriptionCreatedBefore,
            HookSource.Api,
            null,
            customer.Id,
            null,
            proposed);
        if (before?.New != null)
        {
            ApplySubscriptionDtoMutation.Apply(record, before.New, allowKeyChange: true);

            // Re-resolve FKs when hooks change customer or billing cycle keys
            if (!string.Equals(before.New.CustomerKey, customer.Key, StringComparison.Ordinal))
            {
                customer = await _customerRepository.FindByKeyAsync(before.New.CustomerKey)
                    ?? throw new NotFoundException($"Customer with key '{before.New.CustomerKey}' not found");
                record.CustomerId = customer.Id;
            }

            if (!string.Equals(before.New.BillingCycleKey, billingCycle.Key, StringComparison.Ordinal))
            {
                billingCycle = await _billingCycleRepository.FindByKeyAsync(before.New.BillingCycleKey)
                    ?? throw new NotFoundException($"Billing cycle with key '{before.New.BillingCycleKey}' not found");
                plan = await _planRepository.FindByIdAsync(billingCycle.PlanId)
                    ?? throw new NotFoundException($"Plan not found for billing cycle '{before.New.BillingCycleKey}'");
                record.BillingCycleId = billingCycle.Id;
                record.PlanId = plan.Id;
                product = await _productRepository.FindByIdAsync(plan.ProductId)
                    ?? throw new NotFoundException($"Product not found for plan '{plan.Key}'");
            }

            // Re-validate after before-hook mutations
            var revalidateDto = new CreateSubscriptionDto(
                record.Key,
                before.New.CustomerKey,
                before.New.BillingCycleKey,
                record.ActivationDate,
                record.ExpirationDate,
                record.CancellationDate,
                record.TrialEndDate,
                record.CurrentPeriodStart,
                record.CurrentPeriodEnd,
                record.StripeSubscriptionId,
                record.Metadata);
            var revalidation = await _createValidator.ValidateAsync(revalidateDto);
            if (!revalidation.IsValid)
            {
                throw new ValidationException(
                    "Invalid subscription data after hook mutation",
                    revalidation.Errors
                );
            }

            if (record.Key != dto.Key)
            {
                var keyTaken = await _subscriptionRepository.FindByKeyAsync(record.Key);
                if (keyTaken != null)
                {
                    throw new ConflictException($"Subscription with key '{record.Key}' already exists");
                }
            }

            if (record.StripeSubscriptionId != null &&
                record.StripeSubscriptionId != dto.StripeSubscriptionId)
            {
                var existingStripe = await _subscriptionRepository.FindByStripeIdAsync(record.StripeSubscriptionId);
                if (existingStripe != null)
                {
                    throw new ConflictException($"Subscription with Stripe ID '{record.StripeSubscriptionId}' already exists");
                }
            }
        }

        var savedRecord = await _subscriptionRepository.SaveAsync(record);

        // Load from view to get computed status for DTO
        var viewRecord = await _subscriptionRepository.FindByKeyAsync(savedRecord.Key);
        if (viewRecord == null)
        {
            throw new NotFoundException("Failed to load created subscription");
        }

        var keys = await ResolveSubscriptionKeysAsync(viewRecord);
        var overrides = await LoadFeatureOverridesAsync(viewRecord.Id);

        var subscription = SubscriptionMapper.ToDomain(viewRecord, overrides);
        var savedDto = await ToDtoAsync(viewRecord);

        await _hooks.EmitSubscriptionAfterAsync(
            HookEvents.SubscriptionCreatedAfter,
            HookSource.Api,
            savedRecord.Id,
            customer.Id,
            null,
            savedDto);
        return savedDto;
    }

    public async Task<SubscriptionDto> UpdateSubscriptionAsync(string subscriptionKey, UpdateSubscriptionDto dto)
    {
        var validationResult = await _updateValidator.ValidateAsync(dto);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(
                "Invalid update data",
                validationResult.Errors
            );
        }

        // Check if trialEndDate should be updated (set or explicitly cleared)
        var shouldUpdateTrialEndDate = dto.TrialEndDate != null || dto.ClearTrialEndDate;

        // Load tracked record for updates
        var record = await _subscriptionRepository.FindByKeyForUpdateAsync(subscriptionKey);
        if (record == null)
        {
            throw new NotFoundException($"Subscription with key '{subscriptionKey}' not found");
        }

        // Block updates if subscription is archived
        if (record.IsArchived)
        {
            throw new DomainException(
                $"Cannot update archived subscription with key '{subscriptionKey}'. " +
                "Please unarchive the subscription first."
            );
        }

        var oldView = await _subscriptionRepository.FindByKeyAsync(subscriptionKey);
        var oldDto = oldView != null ? await ToDtoAsync(oldView) : null;

        // Update properties directly on record (activationDate is immutable)
        if (dto.ExpirationDate != null)
        {
            record.ExpirationDate = dto.ExpirationDate;
        }
        if (dto.CancellationDate != null)
        {
            record.CancellationDate = dto.CancellationDate;
        }
        // Handle trialEndDate updates — omitted leaves existing value alone
        if (shouldUpdateTrialEndDate)
        {
            record.TrialEndDate = dto.ClearTrialEndDate ? null : dto.TrialEndDate;
        }
        if (dto.CurrentPeriodStart != null)
        {
            record.CurrentPeriodStart = dto.CurrentPeriodStart;
        }
        if (dto.CurrentPeriodEnd != null)
        {
            record.CurrentPeriodEnd = dto.CurrentPeriodEnd;
        }
        if (dto.Metadata != null)
        {
            record.Metadata = dto.Metadata;
        }
        if (dto.StripeSubscriptionId != null)
        {
            record.StripeSubscriptionId = dto.StripeSubscriptionId;
        }
        if (dto.BillingCycleKey != null)
        {
            // Find the new billing cycle
            var billingCycle = await _billingCycleRepository.FindByKeyAsync(dto.BillingCycleKey);
            if (billingCycle == null)
            {
                throw new NotFoundException($"Billing cycle with key '{dto.BillingCycleKey}' not found");
            }

            record.BillingCycleId = billingCycle.Id;
            record.PlanId = billingCycle.PlanId; // Update plan ID to match new billing cycle
        }

        record.UpdatedAt = DateHelper.Now();

        var customerRec = await _customerRepository.FindByIdAsync(record.CustomerId);
        var planRec = await _planRepository.FindByIdAsync(record.PlanId);
        var cycleRec = await _billingCycleRepository.FindByIdAsync(record.BillingCycleId);
        var productRec = planRec != null ? await _productRepository.FindByIdAsync(planRec.ProductId) : null;
        if (customerRec != null && planRec != null && cycleRec != null && productRec != null)
        {
            var proposed = SubscriptionMapper.ToDto(
                new Subscription(new SubscriptionProps
                {
                    Key = record.Key,
                    CustomerId = record.CustomerId,
                    PlanId = record.PlanId,
                    BillingCycleId = record.BillingCycleId,
                    Status = SubscriptionStatus.Active,
                    IsArchived = record.IsArchived,
                    ActivationDate = record.ActivationDate,
                    ExpirationDate = record.ExpirationDate,
                    CancellationDate = record.CancellationDate,
                    TrialEndDate = record.TrialEndDate,
                    CurrentPeriodStart = record.CurrentPeriodStart,
                    CurrentPeriodEnd = record.CurrentPeriodEnd,
                    StripeSubscriptionId = record.StripeSubscriptionId,
                    FeatureOverrides = new List<FeatureOverride>(),
                    Metadata = record.Metadata,
                    CreatedAt = record.CreatedAt,
                    UpdatedAt = record.UpdatedAt
                }),
                customerRec.Key,
                productRec.Key,
                planRec.Key,
                cycleRec.Key
            );
            var before = await _hooks.EmitSubscriptionBeforeAsync(
                HookEvents.SubscriptionUpdatedBefore,
                HookSource.Api,
                record.Id,
                record.CustomerId,
                oldDto,
                proposed);
            if (before?.New != null)
            {
                ApplySubscriptionDtoMutation.Apply(record, before.New, allowKeyChange: false);
            }
        }

        // Save the same tracked record
        var savedRecord = await _subscriptionRepository.SaveAsync(record);

        // Load from view to get computed status for DTO
        var viewRecord = await _subscriptionRepository.FindByKeyAsync(savedRecord.Key);
        if (viewRecord == null)
        {
            throw new NotFoundException("Failed to load updated subscription");
        }

        var keys = await ResolveSubscriptionKeysAsync(viewRecord);
        var overrides = await LoadFeatureOverridesAsync(viewRecord.Id);

        var subscription = SubscriptionMapper.ToDomain(viewRecord, overrides);
        var savedDto = await ToDtoAsync(viewRecord);
        await _hooks.EmitSubscriptionAfterAsync(
            HookEvents.SubscriptionUpdatedAfter,
            HookSource.Api,
            savedRecord.Id,
            savedRecord.CustomerId,
            oldDto,
            savedDto);
        return savedDto;
    }

    public async Task<SubscriptionDto?> GetSubscriptionAsync(string subscriptionKey)
    {
        var viewRecord = await _subscriptionRepository.FindByKeyAsync(subscriptionKey);
        if (viewRecord == null)
            return null;

        var keys = await ResolveSubscriptionKeysAsync(viewRecord);
        var overrides = await LoadFeatureOverridesAsync(viewRecord.Id);

        var subscription = SubscriptionMapper.ToDomain(viewRecord, overrides);
        return await ToDtoAsync(viewRecord);
    }

    /// <summary>
    /// Resolve filter keys to IDs for database querying
    /// Returns null if any required entity is not found (to indicate empty result)
    /// </summary>
    private async Task<Dictionary<string, object?>?> ResolveFilterKeysAsync(SubscriptionFilterDto filters)
    {
        var resolved = new Dictionary<string, object?>();

        // Resolve customerKey to customerId
        if (filters.CustomerKey != null)
        {
            var customer = await _customerRepository.FindByKeyAsync(filters.CustomerKey);
            if (customer == null)
            {
                // Customer not found - return null to indicate empty result
                return null;
            }
            resolved["customerId"] = customer.Id;
        }

        // Resolve planKey and/or productKey to planIds
        if (filters.PlanKey != null)
        {
            if (filters.ProductKey != null)
            {
                // Both planKey and productKey - find specific plan
                var plan = await _planRepository.FindByKeyAsync(filters.PlanKey);
                if (plan == null)
                {
                    return null;
                }
                // Check if plan belongs to product
                var product = await _productRepository.FindByIdAsync(plan.ProductId);
                if (product == null || product.Key != filters.ProductKey)
                {
                    // Plan doesn't belong to product - return null to indicate empty result
                    return null;
                }
                resolved["planId"] = plan.Id;
            }
            else
            {
                // Only planKey - plan keys are globally unique, so findByKey is sufficient
                var plan = await _planRepository.FindByKeyAsync(filters.PlanKey);
                if (plan == null)
                {
                    return null;
                }
                resolved["planId"] = plan.Id;
            }
        }
        else if (filters.ProductKey != null)
        {
            // Only productKey - find all plans for this product
            var product = await _productRepository.FindByKeyAsync(filters.ProductKey);
            if (product == null)
            {
                resolved["planIds"] = new List<long>();
                return resolved;
            }
            var plans = await _planRepository.FindByProductAsync(product.Key);
            if (plans.Count == 0)
            {
                resolved["planIds"] = new List<long>();
                return resolved;
            }
            var planIds = plans.Select(p => p.Id).ToList();
            resolved["planIds"] = planIds;
        }

        return resolved;
    }

    /// <summary>
    /// Resolve filter keys to IDs for database querying (detailed filters)
    /// Returns null if any required entity is not found (to indicate empty result)
    /// </summary>
    private async Task<Dictionary<string, object?>?> ResolveDetailedFilterKeysAsync(DetailedSubscriptionFilterDto filters)
    {
        var resolved = new Dictionary<string, object?>();

        // Resolve customerKey to customerId
        if (filters.CustomerKey != null)
        {
            var customer = await _customerRepository.FindByKeyAsync(filters.CustomerKey);
            if (customer == null)
            {
                return null;
            }
            resolved["customerId"] = customer.Id;
        }

        // Resolve planKey and/or productKey to planIds
        if (filters.PlanKey != null)
        {
            if (filters.ProductKey != null)
            {
                var plan = await _planRepository.FindByKeyAsync(filters.PlanKey);
                if (plan == null)
                {
                    return null;
                }
                // Check if plan belongs to product
                var product = await _productRepository.FindByIdAsync(plan.ProductId);
                if (product == null || product.Key != filters.ProductKey)
                {
                    return null;
                }
                resolved["planId"] = plan.Id;
            }
            else
            {
                var plan = await _planRepository.FindByKeyAsync(filters.PlanKey);
                if (plan == null)
                {
                    return null;
                }
                resolved["planId"] = plan.Id;
            }
        }
        else if (filters.ProductKey != null)
        {
            var product = await _productRepository.FindByKeyAsync(filters.ProductKey);
            if (product == null)
            {
                resolved["planIds"] = new List<long>();
                return resolved;
            }
            var plans = await _planRepository.FindByProductAsync(product.Key);
            if (plans.Count == 0)
            {
                resolved["planIds"] = new List<long>();
                return resolved;
            }
            var planIds = plans.Select(p => p.Id).ToList();
            resolved["planIds"] = planIds;
        }

        // Resolve billingCycleKey to billingCycleId
        if (filters.BillingCycleKey != null)
        {
            var billingCycle = await _billingCycleRepository.FindByKeyAsync(filters.BillingCycleKey);
            if (billingCycle == null)
            {
                return null;
            }
            resolved["billingCycleId"] = billingCycle.Id;
        }

        // Copy other filter properties (date ranges, etc.)
        if (filters.ActivationDateFrom != null)
        {
            resolved["activationDateFrom"] = filters.ActivationDateFrom.Value;
        }
        if (filters.ActivationDateTo != null)
        {
            resolved["activationDateTo"] = filters.ActivationDateTo.Value;
        }
        if (filters.ExpirationDateFrom != null)
        {
            resolved["expirationDateFrom"] = filters.ExpirationDateFrom.Value;
        }
        if (filters.ExpirationDateTo != null)
        {
            resolved["expirationDateTo"] = filters.ExpirationDateTo.Value;
        }
        if (filters.TrialEndDateFrom != null)
        {
            resolved["trialEndDateFrom"] = filters.TrialEndDateFrom.Value;
        }
        if (filters.TrialEndDateTo != null)
        {
            resolved["trialEndDateTo"] = filters.TrialEndDateTo.Value;
        }
        if (filters.CurrentPeriodStartFrom != null)
        {
            resolved["currentPeriodStartFrom"] = filters.CurrentPeriodStartFrom.Value;
        }
        if (filters.CurrentPeriodStartTo != null)
        {
            resolved["currentPeriodStartTo"] = filters.CurrentPeriodStartTo.Value;
        }
        if (filters.CurrentPeriodEndFrom != null)
        {
            resolved["currentPeriodEndFrom"] = filters.CurrentPeriodEndFrom.Value;
        }
        if (filters.CurrentPeriodEndTo != null)
        {
            resolved["currentPeriodEndTo"] = filters.CurrentPeriodEndTo.Value;
        }
        if (filters.HasStripeId != null)
        {
            resolved["hasStripeId"] = filters.HasStripeId.Value;
        }
        if (filters.HasTrial != null)
        {
            resolved["hasTrial"] = filters.HasTrial.Value;
        }

        // Pass through isArchived filter
        if (filters.IsArchived != null)
        {
            resolved["isArchived"] = filters.IsArchived.Value;
        }

        return resolved;
    }

    public async Task<List<SubscriptionDto>> ListSubscriptionsAsync(SubscriptionFilterDto? filters = null)
    {
        var filterDto = filters ?? new SubscriptionFilterDto();
        var validationResult = await _filterValidator.ValidateAsync(filterDto);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(
                "Invalid filter parameters",
                validationResult.Errors
            );
        }

        // Resolve keys early so missing keys return empty (not an unfiltered page)
        var resolvedFilters = await ResolveFilterKeysAsync(filterDto);

        if (resolvedFilters == null ||
            (resolvedFilters.ContainsKey("planIds") && resolvedFilters["planIds"] is List<long> planIds && planIds.Count == 0))
        {
            return new List<SubscriptionDto>();
        }

        // Pass original keys through so the repository can filter in SQL
        var dbFilters = new SubscriptionFilterDto(
            CustomerKey: filterDto.CustomerKey,
            ProductKey: filterDto.ProductKey,
            PlanKey: filterDto.PlanKey,
            Status: filterDto.Status,
            IsArchived: filterDto.IsArchived,
            SortBy: filterDto.SortBy,
            SortOrder: filterDto.SortOrder,
            Limit: filterDto.Limit,
            Offset: filterDto.Offset
        );

        var results = await _subscriptionRepository.FindAllAsync(dbFilters);

        // Map to DTOs
        var dtos = new List<SubscriptionDto>();
        foreach (var result in results)
        {
            // Get keys for plan, product, billing cycle (customer is already available from join)
            var keys = await ResolveSubscriptionKeysAsync(result.Subscription);

            // Convert CustomerRecord to domain entity for DTO mapping
            var customerDto = result.Customer != null
                ? CustomerMapper.ToDto(CustomerMapper.ToDomain(result.Customer))
                : null;

            // Convert SubscriptionStatusViewRecord to domain entity for DTO mapping
            var overrideList = await LoadFeatureOverridesAsync(result.Subscription.Id);
            var subscription = SubscriptionMapper.ToDomain(result.Subscription, overrideList);

            var dto = await ToDtoAsync(result.Subscription);
            dto.Customer = customerDto;
            dtos.Add(dto);
        }
        return dtos;
    }

    public async Task<List<SubscriptionDto>> FindSubscriptionsAsync(DetailedSubscriptionFilterDto filters)
    {
        var validationResult = await _detailedFilterValidator.ValidateAsync(filters);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(
                "Invalid filter parameters",
                validationResult.Errors
            );
        }

        // Resolve keys early so missing keys return empty
        var resolvedFilters = await ResolveDetailedFilterKeysAsync(filters);

        if (resolvedFilters == null ||
            (resolvedFilters.ContainsKey("planIds") && resolvedFilters["planIds"] is List<long> planIds && planIds.Count == 0))
        {
            return new List<SubscriptionDto>();
        }

        var results = await _subscriptionRepository.FindDetailedAsync(filters, resolvedFilters);

        // Filter by hasFeatureOverrides (unavoidable post-fetch since it requires loading feature overrides)
        var filteredResults = results;
        if (filters.HasFeatureOverrides != null)
        {
            var hasOverrides = filters.HasFeatureOverrides.Value;
            var filteredList = new List<SubscriptionWithCustomerRecord>();
            foreach (var result in results)
            {
                var hasFeatureOverrides = await _subscriptionRepository.HasFeatureOverridesAsync(result.Subscription.Id);
                if (hasOverrides == hasFeatureOverrides)
                {
                    filteredList.Add(result);
                }
            }
            filteredResults = filteredList;
        }

        // Map to DTOs
        var dtos = new List<SubscriptionDto>();
        foreach (var result in filteredResults)
        {
            var keys = await ResolveSubscriptionKeysAsync(result.Subscription);

            // Convert CustomerRecord to domain entity for DTO mapping
            var customerDto = result.Customer != null
                ? CustomerMapper.ToDto(CustomerMapper.ToDomain(result.Customer))
                : null;

            // Convert SubscriptionStatusViewRecord to domain entity for DTO mapping
            var overrideList = await LoadFeatureOverridesAsync(result.Subscription.Id);
            var subscription = SubscriptionMapper.ToDomain(result.Subscription, overrideList);

            var dto = await ToDtoAsync(result.Subscription);
            dto.Customer = customerDto;
            dtos.Add(dto);
        }
        return dtos;
    }

    public async Task<List<SubscriptionDto>> GetSubscriptionsByCustomerAsync(string customerKey)
    {
        var customer = await _customerRepository.FindByKeyAsync(customerKey);
        if (customer == null)
        {
            throw new NotFoundException($"Customer with key '{customerKey}' not found");
        }

        var subscriptions = await _subscriptionRepository.FindByCustomerIdAsync(customer.Id);

        var dtos = new List<SubscriptionDto>();
        foreach (var subscriptionView in subscriptions)
        {
            var keys = await ResolveSubscriptionKeysAsync(subscriptionView);
            var overrides = await LoadFeatureOverridesAsync(subscriptionView.Id);
            var subscription = SubscriptionMapper.ToDomain(subscriptionView, overrides);
            dtos.Add(await ToDtoAsync(subscriptionView));
        }
        return dtos;
    }

    public async Task ArchiveSubscriptionAsync(string subscriptionKey)
    {
        // Load tracked record for update
        var record = await _subscriptionRepository.FindByKeyForUpdateAsync(subscriptionKey);
        if (record == null)
        {
            throw new NotFoundException($"Subscription with key '{subscriptionKey}' not found");
        }

        var oldView = await _subscriptionRepository.FindByKeyAsync(subscriptionKey);
        if (oldView == null)
        {
            throw new NotFoundException($"Subscription with key '{subscriptionKey}' not found");
        }
        var oldDto = await ToDtoAsync(oldView);
        var proposed = oldDto.Clone();
        proposed.IsArchived = true;
        proposed.UpdatedAt = DateHelper.Now().ToUniversalTime().ToString("O");
        var before = await _hooks.EmitSubscriptionBeforeAsync(
            HookEvents.SubscriptionArchivedBefore,
            HookSource.Api,
            record.Id,
            record.CustomerId,
            oldDto,
            proposed);
        if (before?.New != null)
        {
            ApplySubscriptionDtoMutation.Apply(record, before.New, allowKeyChange: false);
        }

        // Simple property update - modify record directly
        record.IsArchived = true;
        record.UpdatedAt = DateHelper.Now();
        var saved = await _subscriptionRepository.SaveAsync(record);
        var savedView = await _subscriptionRepository.FindByKeyAsync(saved.Key)
            ?? throw new NotFoundException("Failed to load archived subscription");
        await _hooks.EmitSubscriptionAfterAsync(
            HookEvents.SubscriptionArchivedAfter,
            HookSource.Api,
            saved.Id,
            saved.CustomerId,
            oldDto,
            await ToDtoAsync(savedView));
    }

    public async Task UnarchiveSubscriptionAsync(string subscriptionKey)
    {
        var subscription = await _subscriptionRepository.FindByKeyAsync(subscriptionKey);
        if (subscription == null)
        {
            throw new NotFoundException($"Subscription with key '{subscriptionKey}' not found");
        }

        // Load tracked record for update
        var record = await _subscriptionRepository.FindByKeyForUpdateAsync(subscriptionKey);
        if (record == null)
        {
            throw new NotFoundException($"Subscription with key '{subscriptionKey}' not found");
        }

        var oldDto = await ToDtoAsync(subscription);
        var proposed = oldDto.Clone();
        proposed.IsArchived = false;
        proposed.UpdatedAt = DateHelper.Now().ToUniversalTime().ToString("O");
        var before = await _hooks.EmitSubscriptionBeforeAsync(
            HookEvents.SubscriptionUnarchivedBefore,
            HookSource.Api,
            record.Id,
            record.CustomerId,
            oldDto,
            proposed);
        if (before?.New != null)
        {
            ApplySubscriptionDtoMutation.Apply(record, before.New, allowKeyChange: false);
        }

        // Simple property update - modify record directly
        record.IsArchived = false;
        record.UpdatedAt = DateHelper.Now();
        var saved = await _subscriptionRepository.SaveAsync(record);
        var savedView = await _subscriptionRepository.FindByKeyAsync(saved.Key)
            ?? throw new NotFoundException("Failed to load unarchived subscription");
        await _hooks.EmitSubscriptionAfterAsync(
            HookEvents.SubscriptionUnarchivedAfter,
            HookSource.Api,
            saved.Id,
            saved.CustomerId,
            oldDto,
            await ToDtoAsync(savedView));
    }

    public async Task DeleteSubscriptionAsync(string subscriptionKey)
    {
        var subscription = await _subscriptionRepository.FindByKeyAsync(subscriptionKey);
        if (subscription == null)
        {
            throw new NotFoundException($"Subscription with key '{subscriptionKey}' not found");
        }

        var oldDto = await ToDtoAsync(subscription);
        await _hooks.EmitSubscriptionBeforeAsync(
            HookEvents.SubscriptionDeletedBefore,
            HookSource.Api,
            subscription.Id,
            subscription.CustomerId,
            oldDto,
            null);

        // No deletion constraint - subscriptions can be deleted regardless of status
        await _subscriptionRepository.DeleteAsync(subscription.Id);

        await _hooks.EmitSubscriptionAfterAsync(
            HookEvents.SubscriptionDeletedAfter,
            HookSource.Api,
            subscription.Id,
            subscription.CustomerId,
            oldDto,
            null);
    }

    private DateTime? ValidateExpiry(string type, DateTime? expiry)
    {
        if (type is not ("permanent" or "temporary" or "timed"))
            throw new ValidationException("Invalid override type");
        if (type != "timed")
        {
            if (expiry != null)
                throw new ValidationException("Only timed overrides accept expiresAt");
            return null;
        }
        if (expiry == null || expiry.Value.Kind == DateTimeKind.Unspecified || expiry.Value.ToUniversalTime() <= Clock.UtcNow)
            throw new ValidationException("Timed overrides require a future expiry with a timezone");
        return expiry.Value.ToUniversalTime();
    }
    public async Task AddFeatureOverrideAsync(
        string subscriptionKey,
        string featureKey,
        string value,
        OverrideType overrideType = OverrideType.Permanent, DateTime? expiresAt = null)
    {
        var subscriptionView = await _subscriptionRepository.FindByKeyAsync(subscriptionKey);
        if (subscriptionView == null)
        {
            throw new NotFoundException($"Subscription with key '{subscriptionKey}' not found");
        }

        // Block updates if subscription is archived
        if (subscriptionView.IsArchived)
        {
            throw new DomainException(
                $"Cannot add feature override to archived subscription with key '{subscriptionKey}'. " +
                "Please unarchive the subscription first."
            );
        }

        var featureRecord = await _featureRepository.FindByKeyAsync(featureKey);
        if (featureRecord == null)
        {
            throw new NotFoundException($"Feature with key '{featureKey}' not found");
        }

        // Convert to domain entity for validation
        var feature = FeatureMapper.ToDomain(featureRecord);
        FeatureValueValidator.Validate(value, feature.Props.ValueType);

        var oldDto = await ToDtoAsync(subscriptionView);
        var proposed = oldDto.Clone();
        proposed.UpdatedAt = DateHelper.Now().ToUniversalTime().ToString("O");
        var overrideTypeWire = overrideType.ToString().ToLowerInvariant();
        ValidateExpiry(overrideTypeWire, expiresAt);
        var before = await _hooks.EmitSubscriptionBeforeAsync(
            HookEvents.SubscriptionFeatureOverrideAddedBefore,
            HookSource.Api,
            subscriptionView.Id,
            subscriptionView.CustomerId,
            oldDto,
            proposed,
            featureKey,
            value,
            overrideTypeWire, expiresAt: expiresAt);
        var valueToApply = before?.Value ?? value;
        var typeToApply = before?.OverrideType ?? overrideTypeWire;
        if (before?.New != null)
        {
            var record = await _subscriptionRepository.FindByKeyForUpdateAsync(subscriptionKey);
            if (record != null)
            {
                ApplySubscriptionDtoMutation.Apply(record, before.New, allowKeyChange: false);
                await _subscriptionRepository.SaveAsync(record);
            }
        }

        FeatureValueValidator.Validate(valueToApply, feature.Props.ValueType);

        // Save feature override directly to database
        await _subscriptionRepository.AddFeatureOverrideAsync(
            subscriptionView.Id,
            featureRecord.Id,
            valueToApply,
            typeToApply,
            ValidateExpiry(typeToApply, before != null ? before.ExpiresAt : expiresAt)
        );

        var savedView = await _subscriptionRepository.FindByKeyAsync(subscriptionKey)
            ?? throw new NotFoundException($"Subscription with key '{subscriptionKey}' not found");
        await _hooks.EmitSubscriptionAfterAsync(
            HookEvents.SubscriptionFeatureOverrideAddedAfter,
            HookSource.Api,
            savedView.Id,
            savedView.CustomerId,
            oldDto,
            await ToDtoAsync(savedView),
            featureKey,
            valueToApply,
            typeToApply, expiresAt: before != null ? before.ExpiresAt : expiresAt);
    }

    public async Task RemoveFeatureOverrideAsync(string subscriptionKey, string featureKey)
    {
        var subscriptionView = await _subscriptionRepository.FindByKeyAsync(subscriptionKey);
        if (subscriptionView == null)
        {
            throw new NotFoundException($"Subscription with key '{subscriptionKey}' not found");
        }

        // Block updates if subscription is archived
        if (subscriptionView.IsArchived)
        {
            throw new DomainException(
                $"Cannot remove feature override from archived subscription with key '{subscriptionKey}'. " +
                "Please unarchive the subscription first."
            );
        }

        var featureRecord = await _featureRepository.FindByKeyAsync(featureKey);
        if (featureRecord == null)
        {
            throw new NotFoundException($"Feature with key '{featureKey}' not found");
        }

        var oldDto = await ToDtoAsync(subscriptionView);
        var proposed = oldDto.Clone();
        proposed.UpdatedAt = DateHelper.Now().ToUniversalTime().ToString("O");
        var before = await _hooks.EmitSubscriptionBeforeAsync(
            HookEvents.SubscriptionFeatureOverrideRemovedBefore,
            HookSource.Api,
            subscriptionView.Id,
            subscriptionView.CustomerId,
            oldDto,
            proposed,
            featureKey);
        if (before?.New != null)
        {
            var record = await _subscriptionRepository.FindByKeyForUpdateAsync(subscriptionKey);
            if (record != null)
            {
                ApplySubscriptionDtoMutation.Apply(record, before.New, allowKeyChange: false);
                await _subscriptionRepository.SaveAsync(record);
            }
        }

        // Remove feature override directly from database
        await _subscriptionRepository.RemoveFeatureOverrideAsync(
            subscriptionView.Id,
            featureRecord.Id
        );

        var savedView = await _subscriptionRepository.FindByKeyAsync(subscriptionKey)
            ?? throw new NotFoundException($"Subscription with key '{subscriptionKey}' not found");
        await _hooks.EmitSubscriptionAfterAsync(
            HookEvents.SubscriptionFeatureOverrideRemovedAfter,
            HookSource.Api,
            savedView.Id,
            savedView.CustomerId,
            oldDto,
            await ToDtoAsync(savedView),
            featureKey);
    }

    public async Task ClearTemporaryOverridesAsync(string subscriptionKey)
    {
        var subscriptionView = await _subscriptionRepository.FindByKeyAsync(subscriptionKey);
        if (subscriptionView == null)
        {
            throw new NotFoundException($"Subscription with key '{subscriptionKey}' not found");
        }

        // Block updates if subscription is archived
        if (subscriptionView.IsArchived)
        {
            throw new DomainException(
                $"Cannot clear temporary overrides for archived subscription with key '{subscriptionKey}'. " +
                "Please unarchive the subscription first."
            );
        }

        var oldDto = await ToDtoAsync(subscriptionView);
        var proposed = oldDto.Clone();
        proposed.UpdatedAt = DateHelper.Now().ToUniversalTime().ToString("O");
        var before = await _hooks.EmitSubscriptionBeforeAsync(
            HookEvents.SubscriptionTemporaryOverridesClearedBefore,
            HookSource.Api,
            subscriptionView.Id,
            subscriptionView.CustomerId,
            oldDto,
            proposed);
        if (before?.New != null)
        {
            var record = await _subscriptionRepository.FindByKeyForUpdateAsync(subscriptionKey);
            if (record != null)
            {
                ApplySubscriptionDtoMutation.Apply(record, before.New, allowKeyChange: false);
                await _subscriptionRepository.SaveAsync(record);
            }
        }

        // Clear temporary overrides directly from database
        await _subscriptionRepository.ClearTemporaryOverridesAsync(subscriptionView.Id);

        var savedView = await _subscriptionRepository.FindByKeyAsync(subscriptionKey)
            ?? throw new NotFoundException($"Subscription with key '{subscriptionKey}' not found");
        await _hooks.EmitSubscriptionAfterAsync(
            HookEvents.SubscriptionTemporaryOverridesClearedAfter,
            HookSource.Api,
            savedView.Id,
            savedView.CustomerId,
            oldDto,
            await ToDtoAsync(savedView));
    }

    private DateTime? CalculatePeriodEnd(DateTime startDate, BillingCycle billingCycle)
    {
        return billingCycle.CalculateNextPeriodEnd(startDate);
    }

    /// <summary>
    /// Generate versioned subscription key from base key
    /// Examples:
    /// - "sub-abc" -> "sub-abc-v1"
    /// - "sub-abc-v1" -> "sub-abc-v2"
    /// - "sub-abc-v5" -> "sub-abc-v6"
    /// </summary>
    private string GenerateVersionedKey(string baseKey)
    {
        var versionPattern = new System.Text.RegularExpressions.Regex(@"-v(\d+)$");
        var match = versionPattern.Match(baseKey);

        if (match.Success)
        {
            var currentVersion = int.Parse(match.Groups[1].Value);
            var baseKeyWithoutVersion = versionPattern.Replace(baseKey, "");
            return $"{baseKeyWithoutVersion}-v{currentVersion + 1}";
        }
        else
        {
            return $"{baseKey}-v1";
        }
    }

    /// <summary>
    /// Process expired subscriptions and transition them to configured plans.
    /// 
    /// This method:
    /// 1. Finds all expired subscriptions (status='expired', not archived) whose plan has a transition requirement
    /// 2. For each expired subscription:
    ///    - Archives the old subscription
    ///    - Creates a new subscription to the transition billing cycle
    ///    - New subscription key is versioned: original key + "-vX" (or increments if already versioned)
    /// 
    /// Note: Plans do not have grace periods. A subscription is expired when
    /// <c>expirationDate &lt;= NOW()</c> and there is no cancellation.
    /// 
    /// </summary>
    /// <returns>Report of processed subscriptions</returns>
    public async Task<TransitionExpiredSubscriptionsReport> TransitionExpiredSubscriptionsAsync()
    {
        var report = new TransitionExpiredSubscriptionsReport(
            Processed: 0,
            Transitioned: 0,
            Archived: 0,
            Errors: new List<TransitionError>()
        );

        // Find all expired subscriptions with transition plans (optimized query with join)
        var expiredSubscriptions = await _subscriptionRepository.FindExpiredWithTransitionPlansAsync(1000);

        foreach (var expiredSubscription in expiredSubscriptions)
        {
            try
            {
                report = report with
                {
                    Processed = report.Processed + 1
                };

                // Get the plan (already verified to have transition in query, but need it for the key)
                var planRecord = await _planRepository.FindByIdAsync(expiredSubscription.PlanId);
                if (planRecord == null)
                {
                    report = report with
                    {
                        Errors = report.Errors.Append(new TransitionError(
                            expiredSubscription.Key,
                            $"Plan with id '{expiredSubscription.PlanId}' not found"
                        )).ToList()
                    };
                    continue;
                }

                // Plan already verified to have transition requirement in query
                // Transition configured - archive old subscription and create new one
                // Get customer
                var customer = await _customerRepository.FindByIdAsync(expiredSubscription.CustomerId);
                if (customer == null)
                {
                    report = report with
                    {
                        Errors = report.Errors.Append(new TransitionError(
                            expiredSubscription.Key,
                            $"Customer with id '{expiredSubscription.CustomerId}' not found"
                        )).ToList()
                    };
                    continue;
                }

                // Get transition billing cycle
                if (planRecord.OnExpireTransitionToBillingCycleId == null)
                {
                    report = report with
                    {
                        Errors = report.Errors.Append(new TransitionError(
                            expiredSubscription.Key,
                            $"Plan '{planRecord.Id}' does not have onExpireTransitionToBillingCycleId set"
                        )).ToList()
                    };
                    continue;
                }

                var transitionBillingCycle = await _billingCycleRepository.FindByIdAsync(
                    planRecord.OnExpireTransitionToBillingCycleId.Value
                );
                if (transitionBillingCycle == null)
                {
                    report = report with
                    {
                        Errors = report.Errors.Append(new TransitionError(
                            expiredSubscription.Key,
                            $"Billing cycle with id '{planRecord.OnExpireTransitionToBillingCycleId.Value}' not found"
                        )).ToList()
                    };
                    continue;
                }

                // Load tracked record for update (metadata + later archive)
                var expiredRecord = await _subscriptionRepository.FindByKeyForUpdateAsync(expiredSubscription.Key);
                if (expiredRecord == null)
                {
                    report = report with
                    {
                        Errors = report.Errors.Append(new TransitionError(
                            expiredSubscription.Key,
                            "Failed to load subscription for update"
                        )).ToList()
                    };
                    continue;
                }

                // Create replacement BEFORE archiving so create failures leave the old sub intact
                var newSubscriptionKey = GenerateVersionedKey(expiredSubscription.Key);

                // Check if key already exists (shouldn't happen, but be safe)
                var existing = await _subscriptionRepository.FindByKeyAsync(newSubscriptionKey);
                if (existing != null)
                {
                    report = report with
                    {
                        Errors = report.Errors.Append(new TransitionError(
                            expiredSubscription.Key,
                            $"Generated subscription key '{newSubscriptionKey}' already exists"
                        )).ToList()
                    };
                    continue;
                }

                // Create new subscription to transition billing cycle
                var currentPeriodStart = DateHelper.Now();
                // Convert BillingCycleRecord to domain entity for CalculatePeriodEnd
                var transitionBillingCycleDomain = BillingCycleMapper.ToDomain(transitionBillingCycle);
                var currentPeriodEnd = CalculatePeriodEnd(
                    currentPeriodStart,
                    transitionBillingCycleDomain
                );

                // Get plan for transition billing cycle
                var transitionPlan = await _planRepository.FindByIdAsync(transitionBillingCycle.PlanId);
                if (transitionPlan == null)
                {
                    report = report with
                    {
                        Errors = report.Errors.Append(new TransitionError(
                            expiredSubscription.Key,
                            $"Plan not found for transition billing cycle"
                        )).ToList()
                    };
                    continue;
                }

                // Create new subscription record
                var newSubscription = new SubscriptionRecord
                {
                    Id = 0, // Will be set by EF Core
                    Key = newSubscriptionKey,
                    CustomerId = customer.Id,
                    PlanId = transitionPlan.Id,
                    BillingCycleId = transitionBillingCycle.Id,
                    IsArchived = false,
                    ActivationDate = currentPeriodStart,
                    ExpirationDate = null, // New subscription doesn't expire unless set
                    CancellationDate = null,
                    TrialEndDate = null,
                    CurrentPeriodStart = currentPeriodStart,
                    CurrentPeriodEnd = currentPeriodEnd,
                    StripeSubscriptionId = null, // New subscription doesn't have Stripe ID (old archived subscription keeps its Stripe ID)
                    Metadata = expiredRecord.Metadata, // Carry over metadata
                    CreatedAt = DateHelper.Now(),
                    UpdatedAt = DateHelper.Now()
                };

                var transitionProduct = await _productRepository.FindByIdAsync(transitionPlan.ProductId);
                if (transitionProduct != null)
                {
                    var proposed = SubscriptionMapper.ToDto(
                        new Subscription(new SubscriptionProps
                        {
                            Key = newSubscription.Key,
                            CustomerId = newSubscription.CustomerId,
                            PlanId = newSubscription.PlanId,
                            BillingCycleId = newSubscription.BillingCycleId,
                            Status = SubscriptionStatus.Active,
                            IsArchived = false,
                            ActivationDate = newSubscription.ActivationDate,
                            ExpirationDate = newSubscription.ExpirationDate,
                            CancellationDate = newSubscription.CancellationDate,
                            TrialEndDate = newSubscription.TrialEndDate,
                            CurrentPeriodStart = newSubscription.CurrentPeriodStart,
                            CurrentPeriodEnd = newSubscription.CurrentPeriodEnd,
                            StripeSubscriptionId = newSubscription.StripeSubscriptionId,
                            FeatureOverrides = new List<FeatureOverride>(),
                            Metadata = newSubscription.Metadata,
                            CreatedAt = newSubscription.CreatedAt,
                            UpdatedAt = newSubscription.UpdatedAt
                        }),
                        customer.Key,
                        transitionProduct.Key,
                        transitionPlan.Key,
                        transitionBillingCycle.Key
                    );
                    var createBefore = await _hooks.EmitSubscriptionBeforeAsync(
                        HookEvents.SubscriptionCreatedBefore,
                        HookSource.System,
                        null,
                        customer.Id,
                        null,
                        proposed);
                    if (createBefore?.New != null)
                    {
                        ApplySubscriptionDtoMutation.Apply(newSubscription, createBefore.New, allowKeyChange: true);
                    }
                }

                var savedNew = await _subscriptionRepository.SaveAsync(newSubscription);
                var savedNewView = await _subscriptionRepository.FindByKeyAsync(savedNew.Key);
                if (savedNewView != null)
                {
                    await _hooks.EmitSubscriptionAfterAsync(
                        HookEvents.SubscriptionCreatedAfter,
                        HookSource.System,
                        savedNew.Id,
                        savedNew.CustomerId,
                        null,
                        await ToDtoAsync(savedNewView));
                }
                report = report with
                {
                    Transitioned = report.Transitioned + 1
                };

                // Archive old subscription only after replacement was created successfully
                var oldArchivedDto = await ToDtoAsync(expiredSubscription);
                var newArchivedDto = oldArchivedDto.Clone();
                newArchivedDto.IsArchived = true;
                newArchivedDto.UpdatedAt = DateHelper.Now().ToUniversalTime().ToString("O");
                var archiveBefore = await _hooks.EmitSubscriptionBeforeAsync(
                    HookEvents.SubscriptionArchivedBefore,
                    HookSource.System,
                    expiredRecord.Id,
                    expiredRecord.CustomerId,
                    oldArchivedDto,
                    newArchivedDto);
                if (archiveBefore?.New != null)
                {
                    ApplySubscriptionDtoMutation.Apply(expiredRecord, archiveBefore.New, allowKeyChange: false);
                }
                expiredRecord.IsArchived = true;
                expiredRecord.TransitionedAt = DateHelper.Now();
                expiredRecord.UpdatedAt = DateHelper.Now();
                var archivedSaved = await _subscriptionRepository.SaveAsync(expiredRecord);
                var archivedView = await _subscriptionRepository.FindByKeyAsync(archivedSaved.Key);
                if (archivedView != null)
                {
                    await _hooks.EmitSubscriptionAfterAsync(
                        HookEvents.SubscriptionArchivedAfter,
                        HookSource.System,
                        archivedSaved.Id,
                        archivedSaved.CustomerId,
                        oldArchivedDto,
                        await ToDtoAsync(archivedView));
                }
                report = report with
                {
                    Archived = report.Archived + 1
                };
            }
            catch (Exception error)
            {
                report = report with
                {
                    Errors = report.Errors.Append(new TransitionError(
                        expiredSubscription.Key,
                        error.Message
                    )).ToList()
                };
            }
        }

        return report;
    }
}
