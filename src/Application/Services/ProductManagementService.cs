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

public class ProductManagementService
{
    private readonly IProductRepository _productRepository;
    private readonly IFeatureRepository _featureRepository;
    private readonly CreateProductDtoValidator _createValidator;
    private readonly UpdateProductDtoValidator _updateValidator;
    private readonly ProductFilterDtoValidator _filterValidator;

    public ProductManagementService(
        IProductRepository productRepository,
        IFeatureRepository featureRepository,
        CreateProductDtoValidator createValidator,
        UpdateProductDtoValidator updateValidator,
        ProductFilterDtoValidator filterValidator)
    {
        _productRepository = productRepository;
        _featureRepository = featureRepository;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _filterValidator = filterValidator;
    }

    public async Task<ProductDto> CreateProductAsync(CreateProductDto dto)
    {
        // Validate input
        var validationResult = await _createValidator.ValidateAsync(dto);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(
                $"Invalid product data for key '{dto.Key}': {string.Join(", ", validationResult.Errors.Select(e => e.ErrorMessage))}",
                validationResult.Errors
            );
        }

        // Check for duplicate key
        var existing = await _productRepository.FindByKeyAsync(dto.Key);
        if (existing != null)
        {
            throw new ConflictException($"Product with key '{dto.Key}' already exists");
        }

        // Create record from DTO
        var record = new ProductRecord
        {
            Id = 0, // Will be set by EF Core
            Key = dto.Key,
            DisplayName = dto.DisplayName,
            Description = dto.Description,
            Status = ProductStatus.Active.ToString().ToLowerInvariant(),
            Metadata = dto.Metadata,
            CreatedAt = DateHelper.Now(),
            UpdatedAt = DateHelper.Now()
        };

        // Save record
        var savedRecord = await _productRepository.SaveAsync(record);

        // Convert to DTO
        var product = ProductMapper.ToDomain(savedRecord);
        return ProductMapper.ToDto(product);
    }

    public async Task<ProductDto> UpdateProductAsync(string key, UpdateProductDto dto)
    {
        // Validate input
        var validationResult = await _updateValidator.ValidateAsync(dto);
        if (!validationResult.IsValid)
        {
            throw new ValidationException("Invalid update data", validationResult.Errors);
        }

        // Load tracked record
        var record = await _productRepository.FindByKeyAsync(key);
        if (record == null)
        {
            throw new NotFoundException($"Product with key '{key}' not found. Please check the product key and try again.");
        }

        // Convert to domain entity for business rule validation if needed
        var product = ProductMapper.ToDomain(record);
        if (dto.DisplayName != null)
        {
            // Use domain method for validation
            product.UpdateDisplayName(dto.DisplayName);
            // Apply validated change to record
            record.DisplayName = product.DisplayName;
            record.UpdatedAt = product.Props.UpdatedAt;
        }
        if (dto.Description != null)
        {
            record.Description = dto.Description;
            record.UpdatedAt = DateHelper.Now();
        }
        if (dto.Metadata != null)
        {
            record.Metadata = dto.Metadata;
            record.UpdatedAt = DateHelper.Now();
        }

        // Save the same tracked record
        var savedRecord = await _productRepository.SaveAsync(record);

        // Convert to DTO
        var savedProduct = ProductMapper.ToDomain(savedRecord);
        return ProductMapper.ToDto(savedProduct);
    }

    public async Task<ProductDto?> GetProductAsync(string key)
    {
        var record = await _productRepository.FindByKeyAsync(key);
        if (record == null) return null;
        
        var product = ProductMapper.ToDomain(record);
        return ProductMapper.ToDto(product);
    }

    public async Task<List<ProductDto>> ListProductsAsync(ProductFilterDto? filters = null)
    {
        var validationResult = await _filterValidator.ValidateAsync(filters ?? new ProductFilterDto());
        if (!validationResult.IsValid)
        {
            throw new ValidationException("Invalid filter parameters", validationResult.Errors);
        }

        var records = await _productRepository.FindAllAsync(filters ?? new ProductFilterDto());
        return records.Select(r => ProductMapper.ToDto(ProductMapper.ToDomain(r))).ToList();
    }

    public async Task DeleteProductAsync(string key)
    {
        var record = await _productRepository.FindByKeyAsync(key);
        if (record == null)
        {
            throw new NotFoundException($"Product with key '{key}' not found");
        }

        // Convert to domain entity for business rule validation
        var product = ProductMapper.ToDomain(record);
        if (!product.CanDelete())
        {
            throw new DomainException(
                $"Cannot delete product with status '{product.Status}'. Product must be archived before deletion."
            );
        }

        // Check for plans before deletion
        var hasPlans = await _productRepository.HasPlansAsync(product.Key);
        if (hasPlans)
        {
            throw new DomainException(
                $"Cannot delete product '{product.Key}'. Product has associated plans. Please delete or archive all plans first."
            );
        }

        await _productRepository.DeleteAsync(record.Id);
    }

    public async Task<ProductDto> ArchiveProductAsync(string key)
    {
        // Load tracked record
        var record = await _productRepository.FindByKeyAsync(key);
        if (record == null)
        {
            throw new NotFoundException($"Product with key '{key}' not found");
        }

        // Simple property update - modify record directly
        record.Status = ProductStatus.Archived.ToString().ToLowerInvariant();
        record.UpdatedAt = DateHelper.Now();

        var savedRecord = await _productRepository.SaveAsync(record);
        var product = ProductMapper.ToDomain(savedRecord);
        return ProductMapper.ToDto(product);
    }

    public async Task<ProductDto> UnarchiveProductAsync(string key)
    {
        // Load tracked record
        var record = await _productRepository.FindByKeyAsync(key);
        if (record == null)
        {
            throw new NotFoundException($"Product with key '{key}' not found");
        }

        // Simple property update - modify record directly
        record.Status = ProductStatus.Active.ToString().ToLowerInvariant();
        record.UpdatedAt = DateHelper.Now();

        var savedRecord = await _productRepository.SaveAsync(record);
        var product = ProductMapper.ToDomain(savedRecord);
        return ProductMapper.ToDto(product);
    }

    public async Task AssociateFeatureAsync(string productKey, string featureKey)
    {
        var productRecord = await _productRepository.FindByKeyAsync(productKey);
        if (productRecord == null)
        {
            throw new NotFoundException($"Product with key '{productKey}' not found");
        }

        var featureRecord = await _featureRepository.FindByKeyAsync(featureKey);
        if (featureRecord == null)
        {
            throw new NotFoundException($"Feature with key '{featureKey}' not found");
        }

        await _productRepository.AssociateFeatureAsync(productRecord.Id, featureRecord.Id);
    }

    public async Task DissociateFeatureAsync(string productKey, string featureKey)
    {
        var productRecord = await _productRepository.FindByKeyAsync(productKey);
        if (productRecord == null)
        {
            throw new NotFoundException($"Product with key '{productKey}' not found");
        }

        var featureRecord = await _featureRepository.FindByKeyAsync(featureKey);
        if (featureRecord == null)
        {
            throw new NotFoundException($"Feature with key '{featureKey}' not found");
        }

        await _productRepository.DissociateFeatureAsync(productRecord.Id, featureRecord.Id);
    }
}

