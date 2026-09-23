using Subscrio.Core.Infrastructure.Repositories;
using FluentValidation;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Application.Errors;
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

public class PlanManagementService
{
    internal CatalogReader? Catalog
    {
        get; set;
    }
    private async Task<PlanDto> EnrichAsync(PlanDto dto) => Catalog == null ? dto : dto with { Addons = await Catalog.AddonsAsync(dto.ProductKey) };

    private readonly IPlanRepository _planRepository;
    private readonly IProductRepository _productRepository;
    private readonly IFeatureRepository _featureRepository;
    private readonly IBillingCycleRepository _billingCycleRepository;
    private readonly ISubscriptionRepository _subscriptionRepository;
    private readonly CreatePlanDtoValidator _createValidator;
    private readonly UpdatePlanDtoValidator _updateValidator;
    private readonly PlanFilterDtoValidator _filterValidator;

    public PlanManagementService(
        IPlanRepository planRepository,
        IProductRepository productRepository,
        IFeatureRepository featureRepository,
        IBillingCycleRepository billingCycleRepository,
        ISubscriptionRepository subscriptionRepository,
        CreatePlanDtoValidator createValidator,
        UpdatePlanDtoValidator updateValidator,
        PlanFilterDtoValidator filterValidator)
    {
        _planRepository = planRepository;
        _productRepository = productRepository;
        _featureRepository = featureRepository;
        _billingCycleRepository = billingCycleRepository;
        _subscriptionRepository = subscriptionRepository;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _filterValidator = filterValidator;
    }

    private async Task<(string ProductKey, string? OnExpireTransitionToBillingCycleKey)> ResolvePlanKeysAsync(PlanRecord plan)
    {
        // Get product to resolve ProductKey
        var product = await _productRepository.FindByIdAsync(plan.ProductId);
        if (product == null)
        {
            throw new NotFoundException("Product not found for plan");
        }

        // Get billing cycle to resolve OnExpireTransitionToBillingCycleKey if present
        string? onExpireTransitionToBillingCycleKey = null;
        if (plan.OnExpireTransitionToBillingCycleId != null)
        {
            var billingCycle = await _billingCycleRepository.FindByIdAsync(plan.OnExpireTransitionToBillingCycleId.Value);
            onExpireTransitionToBillingCycleKey = billingCycle?.Key;
        }

        return (product.Key, onExpireTransitionToBillingCycleKey);
    }

    private async Task<List<PlanFeatureValue>> LoadPlanFeatureValuesAsync(long planId)
    {
        var featureValueRecords = await _planRepository.GetFeatureValuesAsync(planId);
        return FeatureValueMapper.ToPlanFeatureValues(featureValueRecords);
    }

    private async Task<List<PlanDto>> MapPlansToDtosAsync(IReadOnlyList<PlanRecord> plans)
    {
        var planIds = plans.Select(p => p.Id).ToList();
        var allFeatureValues = new Dictionary<long, List<PlanFeatureValue>>();
        foreach (var planId in planIds)
        {
            allFeatureValues[planId] = await LoadPlanFeatureValuesAsync(planId);
        }

        var planDtos = new List<PlanDto>();
        foreach (var record in plans)
        {
            var keys = await ResolvePlanKeysAsync(record);
            var featureValues = allFeatureValues.GetValueOrDefault(record.Id, new List<PlanFeatureValue>());
            var plan = PlanMapper.ToDomain(record, keys.ProductKey, keys.OnExpireTransitionToBillingCycleKey, featureValues);
            planDtos.Add(await EnrichAsync(PlanMapper.ToDto(plan, keys.ProductKey, keys.OnExpireTransitionToBillingCycleKey)));
        }
        return planDtos;
    }

    public async Task<PlanDto> CreatePlanAsync(CreatePlanDto dto)
    {
        var validationResult = await _createValidator.ValidateAsync(dto);
        ValidationGuard.EnsureValid(validationResult, "Invalid plan data");

        var product = await ValidationGuard.RequireByKeyAsync(
            _productRepository.FindByKeyAsync, dto.ProductKey, "Product");

        await ValidationGuard.EnsureKeyAvailableAsync(
            _planRepository.FindByKeyAsync, dto.Key, "Plan");

        // Resolve OnExpireTransitionToBillingCycleId if provided
        long? onExpireTransitionToBillingCycleId = null;
        if (dto.OnExpireTransitionToBillingCycleKey != null)
        {
            var billingCycle = await ValidationGuard.RequireByKeyAsync(
                _billingCycleRepository.FindByKeyAsync,
                dto.OnExpireTransitionToBillingCycleKey,
                "Billing cycle");
            onExpireTransitionToBillingCycleId = billingCycle.Id;
        }

        // Create record from DTO
        var record = new PlanRecord
        {
            Id = 0, // Will be set by EF Core
            ProductId = product.Id,
            Key = dto.Key,
            DisplayName = dto.DisplayName,
            Description = dto.Description,
            Status = PlanStatus.Active.ToString().ToLowerInvariant(),
            OnExpireTransitionToBillingCycleId = onExpireTransitionToBillingCycleId,
            Metadata = dto.Metadata,
            CreatedAt = DateHelper.Now(),
            UpdatedAt = DateHelper.Now()
        };

        var savedRecord = await _planRepository.SaveAsync(record);

        // Load feature values (will be empty for new plan)
        var featureValues = await LoadPlanFeatureValuesAsync(savedRecord.Id);

        var keys = await ResolvePlanKeysAsync(savedRecord);
        var plan = PlanMapper.ToDomain(savedRecord, keys.ProductKey, keys.OnExpireTransitionToBillingCycleKey, featureValues);
        return await EnrichAsync(PlanMapper.ToDto(plan, keys.ProductKey, keys.OnExpireTransitionToBillingCycleKey));
    }

    public async Task<PlanDto> UpdatePlanAsync(string planKey, UpdatePlanDto dto)
    {
        var validationResult = await _updateValidator.ValidateAsync(dto);
        ValidationGuard.EnsureValid(validationResult, "Invalid update data");

        var record = await ValidationGuard.RequireByKeyAsync(
            _planRepository.FindByKeyAsync, planKey, "Plan");

        // Load feature values before converting to domain
        var featureValues = await LoadPlanFeatureValuesAsync(record.Id);

        // Convert to domain entity for business rule validation if needed
        var keys = await ResolvePlanKeysAsync(record);
        var plan = PlanMapper.ToDomain(record, keys.ProductKey, keys.OnExpireTransitionToBillingCycleKey, featureValues);

        // Update properties
        if (dto.DisplayName != null)
        {
            plan.UpdateDisplayName(dto.DisplayName);
            record.DisplayName = plan.DisplayName;
            record.UpdatedAt = plan.Props.UpdatedAt;
        }
        if (dto.Description != null)
        {
            record.Description = dto.Description;
            record.UpdatedAt = DateHelper.Now();
        }
        if (dto.ClearOnExpireTransitionToBillingCycleKey)
        {
            record.OnExpireTransitionToBillingCycleId = null;
            record.UpdatedAt = DateHelper.Now();
        }
        else if (dto.OnExpireTransitionToBillingCycleKey != null)
        {
            var billingCycle = await ValidationGuard.RequireByKeyAsync(
                _billingCycleRepository.FindByKeyAsync,
                dto.OnExpireTransitionToBillingCycleKey,
                "Billing cycle");
            record.OnExpireTransitionToBillingCycleId = billingCycle.Id;
            record.UpdatedAt = DateHelper.Now();
        }
        if (dto.Metadata != null)
        {
            record.Metadata = dto.Metadata;
            record.UpdatedAt = DateHelper.Now();
        }
        var savedRecord = await _planRepository.SaveAsync(record);

        // Load feature values after update
        var savedFeatureValues = await LoadPlanFeatureValuesAsync(savedRecord.Id);

        var savedKeys = await ResolvePlanKeysAsync(savedRecord);
        var savedPlan = PlanMapper.ToDomain(savedRecord, savedKeys.ProductKey, savedKeys.OnExpireTransitionToBillingCycleKey, savedFeatureValues);
        return await EnrichAsync(PlanMapper.ToDto(savedPlan, savedKeys.ProductKey, savedKeys.OnExpireTransitionToBillingCycleKey));
    }

    public async Task<PlanDto?> GetPlanAsync(string planKey)
    {
        var record = await _planRepository.FindByKeyAsync(planKey);
        if (record == null)
        {
            return null;
        }

        // Load feature values
        var featureValues = await LoadPlanFeatureValuesAsync(record.Id);

        var keys = await ResolvePlanKeysAsync(record);
        var plan = PlanMapper.ToDomain(record, keys.ProductKey, keys.OnExpireTransitionToBillingCycleKey, featureValues);
        return await EnrichAsync(PlanMapper.ToDto(plan, keys.ProductKey, keys.OnExpireTransitionToBillingCycleKey));
    }

    public async Task<List<PlanDto>> ListPlansAsync(PlanFilterDto? filters = null)
    {
        var filterDto = filters ?? new PlanFilterDto();
        var validationResult = await _filterValidator.ValidateAsync(filterDto);
        ValidationGuard.EnsureValid(validationResult, "Invalid filter parameters");

        // Filters are already validated and use productKey
        var plans = await _planRepository.FindAllAsync(filterDto);
        return await MapPlansToDtosAsync(plans);
    }

    public async Task<List<PlanDto>> GetPlansByProductAsync(string productKey)
    {
        await ValidationGuard.RequireByKeyAsync(
            _productRepository.FindByKeyAsync, productKey, "Product");

        var plans = await _planRepository.FindByProductAsync(productKey);
        return await MapPlansToDtosAsync(plans);
    }

    public async Task ArchivePlanAsync(string planKey)
    {
        var record = await ValidationGuard.RequireByKeyAsync(
            _planRepository.FindByKeyAsync, planKey, "Plan");

        // Simple property update - modify record directly
        record.Status = PlanStatus.Archived.ToString().ToLowerInvariant();
        record.UpdatedAt = DateHelper.Now();
        await _planRepository.SaveAsync(record);
    }

    public async Task UnarchivePlanAsync(string planKey)
    {
        var record = await ValidationGuard.RequireByKeyAsync(
            _planRepository.FindByKeyAsync, planKey, "Plan");

        // Simple property update - modify record directly
        record.Status = PlanStatus.Active.ToString().ToLowerInvariant();
        record.UpdatedAt = DateHelper.Now();
        await _planRepository.SaveAsync(record);
    }

    public async Task DeletePlanAsync(string planKey)
    {
        var record = await ValidationGuard.RequireByKeyAsync(
            _planRepository.FindByKeyAsync, planKey, "Plan");

        // Load feature values (not needed for validation, but for consistency)
        var featureValues = await LoadPlanFeatureValuesAsync(record.Id);

        // Convert to domain entity for business rule validation
        var keys = await ResolvePlanKeysAsync(record);
        var plan = PlanMapper.ToDomain(record, keys.ProductKey, keys.OnExpireTransitionToBillingCycleKey, featureValues);
        if (!plan.CanDelete())
        {
            throw new DomainException(
                $"Cannot delete plan with status '{plan.Status}'. " +
                "Plan must be archived before deletion."
            );
        }

        // Check for subscriptions before deletion (more critical than billing cycles)
        var hasSubscriptions = await _subscriptionRepository.HasSubscriptionsForPlanAsync(record.Id);
        if (hasSubscriptions)
        {
            throw new DomainException(
                $"Cannot delete plan '{plan.Key}'. Plan has active subscriptions. Please cancel or expire all subscriptions first."
            );
        }

        // Check for billing cycles before deletion
        var hasBillingCycles = await _planRepository.HasBillingCyclesAsync(record.Id);
        if (hasBillingCycles)
        {
            throw new DomainException(
                $"Cannot delete plan '{plan.Key}'. Plan has associated billing cycles. Please delete or archive all billing cycles first."
            );
        }

        await _planRepository.DeleteAsync(record.Id);
    }

    public async Task SetFeatureValueAsync(string planKey, string featureKey, string value)
    {
        var planRecord = await ValidationGuard.RequireByKeyAsync(
            _planRepository.FindByKeyAsync, planKey, "Plan");

        var featureRecord = await ValidationGuard.RequireByKeyAsync(
            _featureRepository.FindByKeyAsync, featureKey, "Feature");

        var associatedFeatureIds = await _productRepository.GetFeaturesByProductAsync(planRecord.ProductId);
        if (!associatedFeatureIds.Contains(featureRecord.Id))
        {
            throw new ValidationException(
                $"Feature '{featureKey}' is not associated with the product for plan '{planKey}'. " +
                "Associate the feature with the product before setting a plan value."
            );
        }

        // Convert to domain entity for validation
        var feature = FeatureMapper.ToDomain(featureRecord);
        FeatureValueValidator.Validate(value, feature.Props.ValueType);

        // Save feature value via repository
        await _planRepository.SetFeatureValueAsync(planRecord.Id, featureRecord.Id, value);
    }

    public async Task RemoveFeatureValueAsync(string planKey, string featureKey)
    {
        var planRecord = await ValidationGuard.RequireByKeyAsync(
            _planRepository.FindByKeyAsync, planKey, "Plan");

        var featureRecord = await ValidationGuard.RequireByKeyAsync(
            _featureRepository.FindByKeyAsync, featureKey, "Feature");

        // Remove feature value via repository
        await _planRepository.RemoveFeatureValueAsync(planRecord.Id, featureRecord.Id);
    }

    public async Task<string?> GetFeatureValueAsync(string planKey, string featureKey)
    {
        var planRecord = await ValidationGuard.RequireByKeyAsync(
            _planRepository.FindByKeyAsync, planKey, "Plan");

        var featureRecord = await _featureRepository.FindByKeyAsync(featureKey);
        if (featureRecord == null)
        {
            return null;
        }

        // Get feature value via repository
        return await _planRepository.GetFeatureValueAsync(planRecord.Id, featureRecord.Id);
    }

    public async Task<List<PlanFeatureDto>> GetPlanFeaturesAsync(string planKey)
    {
        var planRecord = await ValidationGuard.RequireByKeyAsync(
            _planRepository.FindByKeyAsync, planKey, "Plan");

        // Get all feature values for this plan
        var featureValueRecords = await _planRepository.GetFeatureValuesAsync(planRecord.Id);

        var dtos = new List<PlanFeatureDto>();
        foreach (var featureValueRecord in featureValueRecords)
        {
            var featureRecord = await _featureRepository.FindByIdAsync(featureValueRecord.FeatureId);
            if (featureRecord != null)
            {
                dtos.Add(new PlanFeatureDto(
                    FeatureKey: featureRecord.Key,
                    Value: featureValueRecord.Value
                ));
            }
        }

        return dtos;
    }
}
