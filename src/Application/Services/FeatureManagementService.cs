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

public class FeatureManagementService
{
    private readonly IFeatureRepository _featureRepository;
    private readonly IProductRepository _productRepository;
    private readonly CreateFeatureDtoValidator _createValidator;
    private readonly UpdateFeatureDtoValidator _updateValidator;
    private readonly FeatureFilterDtoValidator _filterValidator;

    public FeatureManagementService(
        IFeatureRepository featureRepository,
        IProductRepository productRepository,
        CreateFeatureDtoValidator createValidator,
        UpdateFeatureDtoValidator updateValidator,
        FeatureFilterDtoValidator filterValidator)
    {
        _featureRepository = featureRepository;
        _productRepository = productRepository;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _filterValidator = filterValidator;
    }

    public async Task<FeatureDto> CreateFeatureAsync(CreateFeatureDto dto)
    {
        var validationResult = await _createValidator.ValidateAsync(dto);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(
                "Invalid feature data",
                validationResult.Errors
            );
        }

        // Check if key already exists
        var existing = await _featureRepository.FindByKeyAsync(dto.Key);
        if (existing != null)
        {
            throw new ConflictException($"Feature with key '{dto.Key}' already exists");
        }

        // Validate default value based on type
        var valueType = Enum.Parse<FeatureValueType>(dto.ValueType, ignoreCase: true);
        FeatureValueValidator.Validate(dto.DefaultValue, valueType);

        // Create record from DTO
        var record = new FeatureRecord
        {
            Id = 0, // Will be set by EF Core
            Key = dto.Key,
            DisplayName = dto.DisplayName,
            Description = dto.Description,
            ValueType = valueType.ToString().ToLowerInvariant(),
            DefaultValue = dto.DefaultValue,
            GroupName = dto.GroupName,
            Status = FeatureStatus.Active.ToString().ToLowerInvariant(),
            Validator = dto.Validator,
            Metadata = dto.Metadata,
            CreatedAt = DateHelper.Now(),
            UpdatedAt = DateHelper.Now()
        };

        // Save record
        var savedRecord = await _featureRepository.SaveAsync(record);
        var feature = FeatureMapper.ToDomain(savedRecord);
        return FeatureMapper.ToDto(feature);
    }

    public async Task<FeatureDto> UpdateFeatureAsync(string key, UpdateFeatureDto dto)
    {
        var validationResult = await _updateValidator.ValidateAsync(dto);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(
                "Invalid update data",
                validationResult.Errors
            );
        }

        var record = await _featureRepository.FindByKeyAsync(key);
        if (record == null)
        {
            throw new NotFoundException($"Feature with key '{key}' not found");
        }

        // Convert to domain entity for business rule validation if needed
        var feature = FeatureMapper.ToDomain(record);
        if (dto.DisplayName != null)
        {
            feature.UpdateDisplayName(dto.DisplayName);
            record.DisplayName = feature.DisplayName;
            record.UpdatedAt = feature.Props.UpdatedAt;
        }
        if (dto.Description != null)
        {
            record.Description = dto.Description;
            record.UpdatedAt = DateHelper.Now();
        }
        if (dto.DefaultValue != null)
        {
            FeatureValueValidator.Validate(dto.DefaultValue, feature.Props.ValueType);
            record.DefaultValue = dto.DefaultValue;
            record.UpdatedAt = DateHelper.Now();
        }
        if (dto.GroupName != null)
        {
            record.GroupName = dto.GroupName;
            record.UpdatedAt = DateHelper.Now();
        }
        if (dto.Validator != null)
        {
            record.Validator = dto.Validator;
            record.UpdatedAt = DateHelper.Now();
        }
        if (dto.Metadata != null)
        {
            record.Metadata = dto.Metadata;
            record.UpdatedAt = DateHelper.Now();
        }

        var savedRecord = await _featureRepository.SaveAsync(record);
        var savedFeature = FeatureMapper.ToDomain(savedRecord);
        return FeatureMapper.ToDto(savedFeature);
    }

    public async Task<FeatureDto?> GetFeatureAsync(string key)
    {
        var record = await _featureRepository.FindByKeyAsync(key);
        if (record == null) return null;
        
        var feature = FeatureMapper.ToDomain(record);
        return FeatureMapper.ToDto(feature);
    }

    public async Task<List<FeatureDto>> ListFeaturesAsync(FeatureFilterDto? filters = null)
    {
        var filterDto = filters ?? new FeatureFilterDto();
        var validationResult = await _filterValidator.ValidateAsync(filterDto);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(
                "Invalid filter parameters",
                validationResult.Errors
            );
        }

        var records = await _featureRepository.FindAllAsync(filterDto);
        return records.Select(r => FeatureMapper.ToDto(FeatureMapper.ToDomain(r))).ToList();
    }

    public async Task ArchiveFeatureAsync(string key)
    {
        var record = await _featureRepository.FindByKeyAsync(key);
        if (record == null)
        {
            throw new NotFoundException($"Feature with key '{key}' not found");
        }

        // Simple property update - modify record directly
        record.Status = FeatureStatus.Archived.ToString().ToLowerInvariant();
        record.UpdatedAt = DateHelper.Now();
        await _featureRepository.SaveAsync(record);
    }

    public async Task UnarchiveFeatureAsync(string key)
    {
        var record = await _featureRepository.FindByKeyAsync(key);
        if (record == null)
        {
            throw new NotFoundException($"Feature with key '{key}' not found");
        }

        // Simple property update - modify record directly
        record.Status = FeatureStatus.Active.ToString().ToLowerInvariant();
        record.UpdatedAt = DateHelper.Now();
        await _featureRepository.SaveAsync(record);
    }

    public async Task DeleteFeatureAsync(string key)
    {
        var record = await _featureRepository.FindByKeyAsync(key);
        if (record == null)
        {
            throw new NotFoundException($"Feature with key '{key}' not found");
        }

        // Convert to domain entity for business rule validation
        var feature = FeatureMapper.ToDomain(record);
        if (!feature.CanDelete())
        {
            throw new DomainException(
                $"Cannot delete feature with status '{feature.Status}'. " +
                "Feature must be archived before deletion."
            );
        }

        // Check for product associations
        var hasProductAssociations = await _featureRepository.HasProductAssociationsAsync(record.Id);
        if (hasProductAssociations)
        {
            throw new DomainException(
                $"Cannot delete feature '{feature.Key}'. Feature is associated with products. Please dissociate from all products first."
            );
        }

        // Check for plan feature values
        var hasPlanFeatureValues = await _featureRepository.HasPlanFeatureValuesAsync(record.Id);
        if (hasPlanFeatureValues)
        {
            throw new DomainException(
                $"Cannot delete feature '{feature.Key}'. Feature is used in plan feature values. Please remove from all plans first."
            );
        }

        // Check for subscription overrides
        var hasSubscriptionOverrides = await _featureRepository.HasSubscriptionOverridesAsync(record.Id);
        if (hasSubscriptionOverrides)
        {
            throw new DomainException(
                $"Cannot delete feature '{feature.Key}'. Feature has subscription overrides. Please remove all subscription overrides first."
            );
        }

        await _featureRepository.DeleteAsync(record.Id);
    }

    public async Task<List<FeatureDto>> GetFeaturesByProductAsync(string productKey)
    {
        // Verify product exists
        var product = await _productRepository.FindByKeyAsync(productKey);
        if (product == null)
        {
            throw new NotFoundException($"Product with key '{productKey}' not found");
        }

        var features = await _featureRepository.FindByProductAsync(product.Id);
        return features.Select(r => FeatureMapper.ToDto(FeatureMapper.ToDomain(r))).ToList();
    }
}
