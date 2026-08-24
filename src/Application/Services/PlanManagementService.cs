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
        return featureValueRecords.Select(fvr => new PlanFeatureValue
        {
            FeatureId = fvr.FeatureId,
            Value = fvr.Value,
            CreatedAt = fvr.CreatedAt,
            UpdatedAt = fvr.UpdatedAt
        }).ToList();
    }

    public async Task<PlanDto> CreatePlanAsync(CreatePlanDto dto)
    {
        var validationResult = await _createValidator.ValidateAsync(dto);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(
                "Invalid plan data",
                validationResult.Errors
            );
        }

        // Verify product exists by key
        var product = await _productRepository.FindByKeyAsync(dto.ProductKey);
        if (product == null)
        {
            throw new NotFoundException($"Product with key '{dto.ProductKey}' not found");
        }

        // Check if plan key already exists globally
        var existing = await _planRepository.FindByKeyAsync(dto.Key);
        if (existing != null)
        {
            throw new ConflictException($"Plan with key '{dto.Key}' already exists");
        }

        // Resolve OnExpireTransitionToBillingCycleId if provided
        long? onExpireTransitionToBillingCycleId = null;
        if (dto.OnExpireTransitionToBillingCycleKey != null)
        {
            var billingCycle = await _billingCycleRepository.FindByKeyAsync(dto.OnExpireTransitionToBillingCycleKey);
            if (billingCycle == null)
            {
                throw new NotFoundException($"Billing cycle with key '{dto.OnExpireTransitionToBillingCycleKey}' not found");
            }
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

        // Save record
        var savedRecord = await _planRepository.SaveAsync(record);

        // Load feature values (will be empty for new plan)
        var featureValues = await LoadPlanFeatureValuesAsync(savedRecord.Id);

        var keys = await ResolvePlanKeysAsync(savedRecord);
        var plan = PlanMapper.ToDomain(savedRecord, keys.ProductKey, keys.OnExpireTransitionToBillingCycleKey, featureValues);
        return PlanMapper.ToDto(plan, keys.ProductKey, keys.OnExpireTransitionToBillingCycleKey);
    }

    public async Task<PlanDto> UpdatePlanAsync(string planKey, UpdatePlanDto dto)
    {
        var validationResult = await _updateValidator.ValidateAsync(dto);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(
                "Invalid update data",
                validationResult.Errors
            );
        }

        var record = await _planRepository.FindByKeyAsync(planKey);
        if (record == null)
        {
            throw new NotFoundException($"Plan with key '{planKey}' not found");
        }

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
        if (dto.OnExpireTransitionToBillingCycleKey != null)
        {
            var billingCycle = await _billingCycleRepository.FindByKeyAsync(dto.OnExpireTransitionToBillingCycleKey);
            if (billingCycle == null)
            {
                throw new NotFoundException($"Billing cycle with key '{dto.OnExpireTransitionToBillingCycleKey}' not found");
            }
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
        return PlanMapper.ToDto(savedPlan, savedKeys.ProductKey, savedKeys.OnExpireTransitionToBillingCycleKey);
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
        return PlanMapper.ToDto(plan, keys.ProductKey, keys.OnExpireTransitionToBillingCycleKey);
    }

    public async Task<List<PlanDto>> ListPlansAsync(PlanFilterDto? filters = null)
    {
        var filterDto = filters ?? new PlanFilterDto();
        var validationResult = await _filterValidator.ValidateAsync(filterDto);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(
                "Invalid filter parameters",
                validationResult.Errors
            );
        }

        // Filters are already validated and use productKey
        var plans = await _planRepository.FindAllAsync(filterDto);

        // Batch load all plan feature values to avoid N+1 queries
        var planIds = plans.Select(p => p.Id).ToList();
        var allFeatureValues = new Dictionary<long, List<PlanFeatureValue>>();
        foreach (var planId in planIds)
        {
            var featureValues = await LoadPlanFeatureValuesAsync(planId);
            allFeatureValues[planId] = featureValues;
        }

        // Map each plan with resolved keys
        var planDtos = new List<PlanDto>();
        foreach (var record in plans)
        {
            var keys = await ResolvePlanKeysAsync(record);
            var featureValues = allFeatureValues.GetValueOrDefault(record.Id, new List<PlanFeatureValue>());
            var plan = PlanMapper.ToDomain(record, keys.ProductKey, keys.OnExpireTransitionToBillingCycleKey, featureValues);
            planDtos.Add(PlanMapper.ToDto(plan, keys.ProductKey, keys.OnExpireTransitionToBillingCycleKey));
        }
        return planDtos;
    }

    public async Task<List<PlanDto>> GetPlansByProductAsync(string productKey)
    {
        // Verify product exists
        var product = await _productRepository.FindByKeyAsync(productKey);
        if (product == null)
        {
            throw new NotFoundException($"Product with key '{productKey}' not found");
        }

        var plans = await _planRepository.FindByProductAsync(productKey);

        // Batch load all plan feature values to avoid N+1 queries
        var planIds = plans.Select(p => p.Id).ToList();
        var allFeatureValues = new Dictionary<long, List<PlanFeatureValue>>();
        foreach (var planId in planIds)
        {
            var featureValues = await LoadPlanFeatureValuesAsync(planId);
            allFeatureValues[planId] = featureValues;
        }

        // Map each plan with resolved keys
        var planDtos = new List<PlanDto>();
        foreach (var record in plans)
        {
            var keys = await ResolvePlanKeysAsync(record);
            var featureValues = allFeatureValues.GetValueOrDefault(record.Id, new List<PlanFeatureValue>());
            var plan = PlanMapper.ToDomain(record, keys.ProductKey, keys.OnExpireTransitionToBillingCycleKey, featureValues);
            planDtos.Add(PlanMapper.ToDto(plan, keys.ProductKey, keys.OnExpireTransitionToBillingCycleKey));
        }
        return planDtos;
    }

    public async Task ArchivePlanAsync(string planKey)
    {
        var record = await _planRepository.FindByKeyAsync(planKey);
        if (record == null)
        {
            throw new NotFoundException($"Plan with key '{planKey}' not found");
        }

        // Simple property update - modify record directly
        record.Status = PlanStatus.Archived.ToString().ToLowerInvariant();
        record.UpdatedAt = DateHelper.Now();
        await _planRepository.SaveAsync(record);
    }

    public async Task UnarchivePlanAsync(string planKey)
    {
        var record = await _planRepository.FindByKeyAsync(planKey);
        if (record == null)
        {
            throw new NotFoundException($"Plan with key '{planKey}' not found");
        }

        // Simple property update - modify record directly
        record.Status = PlanStatus.Active.ToString().ToLowerInvariant();
        record.UpdatedAt = DateHelper.Now();
        await _planRepository.SaveAsync(record);
    }

    public async Task DeletePlanAsync(string planKey)
    {
        var record = await _planRepository.FindByKeyAsync(planKey);
        if (record == null)
        {
            throw new NotFoundException($"Plan with key '{planKey}' not found");
        }

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
        var planRecord = await _planRepository.FindByKeyAsync(planKey);
        if (planRecord == null)
        {
            throw new NotFoundException($"Plan with key '{planKey}' not found");
        }

        var featureRecord = await _featureRepository.FindByKeyAsync(featureKey);
        if (featureRecord == null)
        {
            throw new NotFoundException($"Feature with key '{featureKey}' not found");
        }

        // Convert to domain entity for validation
        var feature = FeatureMapper.ToDomain(featureRecord);
        FeatureValueValidator.Validate(value, feature.Props.ValueType);

        // Save feature value via repository
        await _planRepository.SetFeatureValueAsync(planRecord.Id, featureRecord.Id, value);
    }

    public async Task RemoveFeatureValueAsync(string planKey, string featureKey)
    {
        var planRecord = await _planRepository.FindByKeyAsync(planKey);
        if (planRecord == null)
        {
            throw new NotFoundException($"Plan with key '{planKey}' not found");
        }

        var featureRecord = await _featureRepository.FindByKeyAsync(featureKey);
        if (featureRecord == null)
        {
            throw new NotFoundException($"Feature with key '{featureKey}' not found");
        }

        // Remove feature value via repository
        await _planRepository.RemoveFeatureValueAsync(planRecord.Id, featureRecord.Id);
    }

    public async Task<string?> GetFeatureValueAsync(string planKey, string featureKey)
    {
        var planRecord = await _planRepository.FindByKeyAsync(planKey);
        if (planRecord == null)
        {
            throw new NotFoundException($"Plan with key '{planKey}' not found");
        }

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
        var planRecord = await _planRepository.FindByKeyAsync(planKey);
        if (planRecord == null)
        {
            throw new NotFoundException($"Plan with key '{planKey}' not found");
        }

        // Get all feature values for this plan
        var featureValueRecords = await _planRepository.GetFeatureValuesAsync(planRecord.Id);
        
        // Convert to DTOs - need to resolve feature keys
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


