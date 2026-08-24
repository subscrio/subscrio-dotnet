using FluentValidation;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Application.Errors;
using Subscrio.Core.Application.Mappers;
using Subscrio.Core.Application.Repositories;
using Subscrio.Core.Application.Validators;
using Subscrio.Core.Domain.Entities;
using Subscrio.Core.Domain.ValueObjects;
using Subscrio.Core.Infrastructure.Database;
using Subscrio.Core.Infrastructure.Utils;
using ValidationException = Subscrio.Core.Application.Errors.ValidationException;

namespace Subscrio.Core.Application.Services;

public class BillingCycleManagementService
{
    private readonly IBillingCycleRepository _billingCycleRepository;
    private readonly IPlanRepository _planRepository;
    private readonly IProductRepository _productRepository;
    private readonly ISubscriptionRepository _subscriptionRepository;
    private readonly CreateBillingCycleDtoValidator _createValidator;
    private readonly UpdateBillingCycleDtoValidator _updateValidator;
    private readonly BillingCycleFilterDtoValidator _filterValidator;

    public BillingCycleManagementService(
        IBillingCycleRepository billingCycleRepository,
        IPlanRepository planRepository,
        IProductRepository productRepository,
        ISubscriptionRepository subscriptionRepository,
        CreateBillingCycleDtoValidator createValidator,
        UpdateBillingCycleDtoValidator updateValidator,
        BillingCycleFilterDtoValidator filterValidator)
    {
        _billingCycleRepository = billingCycleRepository;
        _planRepository = planRepository;
        _productRepository = productRepository;
        _subscriptionRepository = subscriptionRepository;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _filterValidator = filterValidator;
    }

    public async Task<BillingCycleDto> CreateBillingCycleAsync(CreateBillingCycleDto dto)
    {
        var validationResult = await _createValidator.ValidateAsync(dto);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(
                "Invalid billing cycle data",
                validationResult.Errors
            );
        }

        // Verify plan exists
        var plan = await _planRepository.FindByKeyAsync(dto.PlanKey);
        if (plan == null)
        {
            throw new NotFoundException($"Plan with key '{dto.PlanKey}' not found");
        }

        // Check if key already exists globally
        var existing = await _billingCycleRepository.FindByKeyAsync(dto.Key);
        if (existing != null)
        {
            throw new ConflictException($"Billing cycle with key '{dto.Key}' already exists");
        }

        // Validate duration unit
        if (!Enum.TryParse<DurationUnit>(dto.DurationUnit, ignoreCase: true, out var durationUnit))
        {
            throw new ValidationException($"Invalid duration unit: {dto.DurationUnit}");
        }

        // Validate duration value based on duration unit
        if (durationUnit == DurationUnit.Forever)
        {
            // For forever billing cycles, durationValue must be undefined
            if (dto.DurationValue != null)
            {
                throw new ValidationException("Duration value must not be provided for forever billing cycles");
            }
        }
        else
        {
            // For all other duration units, durationValue is required and must be positive
            if (dto.DurationValue == null)
            {
                throw new ValidationException("Duration value is required for non-forever billing cycles");
            }
            if (dto.DurationValue <= 0)
            {
                throw new ValidationException("Duration value must be greater than 0");
            }
        }

        // Create record from DTO
        var record = new BillingCycleRecord
        {
            Id = 0, // Will be set by EF Core
            PlanId = plan.Id,
            Key = dto.Key,
            DisplayName = dto.DisplayName,
            Description = dto.Description,
            Status = BillingCycleStatus.Active.ToString().ToLowerInvariant(),
            DurationValue = dto.DurationValue,
            DurationUnit = durationUnit.ToString().ToLowerInvariant(),
            ExternalProductId = dto.ExternalProductId,
            CreatedAt = DateHelper.Now(),
            UpdatedAt = DateHelper.Now()
        };

        // Save record
        var savedRecord = await _billingCycleRepository.SaveAsync(record);

        // Get product to resolve ProductKey for DTO
        var productRecord = await _productRepository.FindByIdAsync(plan.ProductId);
        if (productRecord == null)
        {
            throw new NotFoundException("Product not found for plan");
        }

        var billingCycle = BillingCycleMapper.ToDomain(savedRecord);
        return BillingCycleMapper.ToDto(billingCycle, productRecord.Key, plan.Key);
    }

    public async Task<BillingCycleDto> UpdateBillingCycleAsync(string key, UpdateBillingCycleDto dto)
    {
        var validationResult = await _updateValidator.ValidateAsync(dto);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(
                "Invalid update data",
                validationResult.Errors
            );
        }

        var record = await _billingCycleRepository.FindByKeyAsync(key);
        if (record == null)
        {
            throw new NotFoundException($"Billing cycle with key '{key}' not found");
        }

        // Update properties directly on record
        if (dto.DisplayName != null)
        {
            record.DisplayName = dto.DisplayName;
        }
        if (dto.Description != null)
        {
            record.Description = dto.Description;
        }
        if (dto.DurationValue != null)
        {
            if (dto.DurationValue <= 0)
            {
                throw new ValidationException("Duration value must be greater than 0");
            }
            record.DurationValue = dto.DurationValue;
        }
        if (dto.DurationUnit != null)
        {
            if (!Enum.TryParse<DurationUnit>(dto.DurationUnit, ignoreCase: true, out var durationUnit))
            {
                throw new ValidationException($"Invalid duration unit: {dto.DurationUnit}");
            }
            record.DurationUnit = durationUnit.ToString().ToLowerInvariant();
        }
        if (dto.ExternalProductId != null)
        {
            record.ExternalProductId = dto.ExternalProductId;
        }

        record.UpdatedAt = DateHelper.Now();
        var savedRecord = await _billingCycleRepository.SaveAsync(record);

        // Get plan and product to resolve keys for DTO
        var plan = await _planRepository.FindByIdAsync(record.PlanId);
        if (plan == null)
        {
            throw new NotFoundException("Plan not found for billing cycle");
        }
        var productRecord = await _productRepository.FindByIdAsync(plan.ProductId);
        if (productRecord == null)
        {
            throw new NotFoundException("Product not found for plan");
        }

        var billingCycle = BillingCycleMapper.ToDomain(savedRecord);
        return BillingCycleMapper.ToDto(billingCycle, productRecord.Key, plan.Key);
    }

    public async Task<BillingCycleDto?> GetBillingCycleAsync(string key)
    {
        var record = await _billingCycleRepository.FindByKeyAsync(key);
        if (record == null)
        {
            return null;
        }

        // Get plan and product to resolve keys for DTO
        var plan = await _planRepository.FindByIdAsync(record.PlanId);
        if (plan == null)
        {
            throw new NotFoundException("Plan not found for billing cycle");
        }
        var productRecord = await _productRepository.FindByIdAsync(plan.ProductId);
        if (productRecord == null)
        {
            throw new NotFoundException("Product not found for plan");
        }

        var billingCycle = BillingCycleMapper.ToDomain(record);
        return BillingCycleMapper.ToDto(billingCycle, productRecord.Key, plan.Key);
    }

    public async Task<List<BillingCycleDto>> GetBillingCyclesByPlanAsync(string planKey)
    {
        // Verify plan exists
        var plan = await _planRepository.FindByKeyAsync(planKey);
        if (plan == null)
        {
            throw new NotFoundException($"Plan with key '{planKey}' not found");
        }

        var billingCycles = await _billingCycleRepository.FindByPlanAsync(plan.Id);
        var productRecord = await _productRepository.FindByIdAsync(plan.ProductId);
        if (productRecord == null)
        {
            throw new NotFoundException("Product not found for plan");
        }
        return billingCycles.Select(bc => 
        {
            var domainBc = BillingCycleMapper.ToDomain(bc);
            return BillingCycleMapper.ToDto(domainBc, productRecord.Key, plan.Key);
        }).ToList();
    }

    public async Task<List<BillingCycleDto>> ListBillingCyclesAsync(BillingCycleFilterDto? filters = null)
    {
        var filterDto = filters ?? new BillingCycleFilterDto();
        var validationResult = await _filterValidator.ValidateAsync(filterDto);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(
                "Invalid filter parameters",
                validationResult.Errors
            );
        }

        // If filtering by plan, get plan first
        if (filterDto.PlanKey != null)
        {
            return await GetBillingCyclesByPlanAsync(filterDto.PlanKey);
        }

        // Otherwise list all (need to resolve productKey/planKey for each)
        var billingCycles = await _billingCycleRepository.FindAllAsync(filterDto);
        var dtos = new List<BillingCycleDto>();

        foreach (var bc in billingCycles)
        {
            var plan = await _planRepository.FindByIdAsync(bc.PlanId);
            if (plan != null)
            {
                var productRecord = await _productRepository.FindByIdAsync(plan.ProductId);
                if (productRecord != null)
                {
                    var domainBc = BillingCycleMapper.ToDomain(bc);
                    dtos.Add(BillingCycleMapper.ToDto(domainBc, productRecord.Key, plan.Key));
                }
            }
        }

        return dtos;
    }

    public async Task ArchiveBillingCycleAsync(string key)
    {
        var record = await _billingCycleRepository.FindByKeyAsync(key);
        if (record == null)
        {
            throw new NotFoundException($"Billing cycle with key '{key}' not found");
        }

        // Simple property update - modify record directly
        record.Status = BillingCycleStatus.Archived.ToString().ToLowerInvariant();
        record.UpdatedAt = DateHelper.Now();
        await _billingCycleRepository.SaveAsync(record);
    }

    public async Task UnarchiveBillingCycleAsync(string key)
    {
        var record = await _billingCycleRepository.FindByKeyAsync(key);
        if (record == null)
        {
            throw new NotFoundException($"Billing cycle with key '{key}' not found");
        }

        // Simple property update - modify record directly
        record.Status = BillingCycleStatus.Active.ToString().ToLowerInvariant();
        record.UpdatedAt = DateHelper.Now();
        await _billingCycleRepository.SaveAsync(record);
    }

    public async Task DeleteBillingCycleAsync(string key)
    {
        var record = await _billingCycleRepository.FindByKeyAsync(key);
        if (record == null)
        {
            throw new NotFoundException($"Billing cycle with key '{key}' not found");
        }

        // Convert to domain entity for business rule validation
        var billingCycle = BillingCycleMapper.ToDomain(record);
        if (!billingCycle.CanDelete())
        {
            throw new DomainException(
                $"Cannot delete billing cycle with status '{billingCycle.Status}'. Billing cycle must be archived before deletion."
            );
        }

        // Check for subscriptions before deletion
        var hasSubscriptions = await _subscriptionRepository.HasSubscriptionsForBillingCycleAsync(record.Id);
        if (hasSubscriptions)
        {
            throw new DomainException(
                $"Cannot delete billing cycle '{billingCycle.Key}'. Billing cycle has active subscriptions. Please cancel or expire all subscriptions first."
            );
        }

        // Check for plan transition references
        var hasPlanTransitionReferences = await _planRepository.HasPlanTransitionReferencesAsync(billingCycle.Key);
        if (hasPlanTransitionReferences)
        {
            throw new DomainException(
                $"Cannot delete billing cycle '{billingCycle.Key}'. Billing cycle is referenced by plan transition settings. Please update or remove plan transition references first."
            );
        }

        await _billingCycleRepository.DeleteAsync(record.Id);
    }

    /// <summary>
    /// Calculate next period end date based on billing cycle
    /// </summary>
    public async Task<DateTime?> CalculateNextPeriodEndAsync(string billingCycleKey, DateTime currentPeriodEnd)
    {
        var record = await _billingCycleRepository.FindByKeyAsync(billingCycleKey);
        if (record == null)
        {
            throw new NotFoundException($"Billing cycle with key '{billingCycleKey}' not found");
        }
        var billingCycle = BillingCycleMapper.ToDomain(record);
        return billingCycle.CalculateNextPeriodEnd(currentPeriodEnd);
    }

    /// <summary>
    /// Get billing cycles by duration unit
    /// </summary>
    public async Task<List<BillingCycleDto>> GetBillingCyclesByDurationUnitAsync(DurationUnit durationUnit)
    {
        var allCycles = await _billingCycleRepository.FindAllAsync(new BillingCycleFilterDto());
        var durationUnitStr = durationUnit.ToString().ToLowerInvariant();
        var filtered = allCycles.Where(cycle => cycle.DurationUnit == durationUnitStr).ToList();
        
        var dtos = new List<BillingCycleDto>();
        foreach (var cycle in filtered)
        {
            var plan = await _planRepository.FindByIdAsync(cycle.PlanId);
            if (plan != null)
            {
                var productRecord = await _productRepository.FindByIdAsync(plan.ProductId);
                if (productRecord != null)
                {
                    var domainCycle = BillingCycleMapper.ToDomain(cycle);
                    dtos.Add(BillingCycleMapper.ToDto(domainCycle, productRecord.Key, plan.Key));
                }
            }
        }
        return dtos;
    }

    /// <summary>
    /// Get default billing cycles (commonly used ones)
    /// </summary>
    public async Task<List<BillingCycleDto>> GetDefaultBillingCyclesAsync()
    {
        var commonCycles = new[]
        {
            new { Key = "monthly", DurationValue = 1, DurationUnit = DurationUnit.Months },
            new { Key = "quarterly", DurationValue = 3, DurationUnit = DurationUnit.Months },
            new { Key = "yearly", DurationValue = 1, DurationUnit = DurationUnit.Years }
        };

        var cycles = new List<BillingCycleDto>();

        foreach (var cycle in commonCycles)
        {
            var existing = await _billingCycleRepository.FindByKeyAsync(cycle.Key);
            if (existing != null)
            {
                var plan = await _planRepository.FindByIdAsync(existing.PlanId);
                if (plan != null)
                {
                    var productRecord = await _productRepository.FindByIdAsync(plan.ProductId);
                    if (productRecord != null)
                    {
                        var domainBc = BillingCycleMapper.ToDomain(existing);
                        cycles.Add(BillingCycleMapper.ToDto(domainBc, productRecord.Key, plan.Key));
                    }
                }
            }
        }

        return cycles;
    }
}
