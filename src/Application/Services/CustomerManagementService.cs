using FluentValidation;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Application.Errors;
using Subscrio.Core.Application.Hooks;
using Subscrio.Core.Application.Mappers;
using Subscrio.Core.Application.Repositories;
using Subscrio.Core.Application.Validators;
using Subscrio.Core.Domain.Entities;
using Subscrio.Core.Domain.ValueObjects;
using Subscrio.Core.Infrastructure.Database;
using Subscrio.Core.Infrastructure.Utils;
using ValidationException = Subscrio.Core.Application.Errors.ValidationException;

namespace Subscrio.Core.Application.Services;

public class CustomerManagementService
{
    private readonly ICustomerRepository _customerRepository;
    private readonly CreateCustomerDtoValidator _createValidator;
    private readonly UpdateCustomerDtoValidator _updateValidator;
    private readonly CustomerFilterDtoValidator _filterValidator;
    private readonly HookDispatcher _hooks;

    public CustomerManagementService(
        ICustomerRepository customerRepository,
        CreateCustomerDtoValidator createValidator,
        UpdateCustomerDtoValidator updateValidator,
        CustomerFilterDtoValidator filterValidator,
        HookDispatcher? hooks = null)
    {
        _customerRepository = customerRepository;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _filterValidator = filterValidator;
        _hooks = hooks ?? new HookDispatcher();
    }

    public async Task<CustomerDto> CreateCustomerAsync(CreateCustomerDto dto)
    {
        var validationResult = await _createValidator.ValidateAsync(dto);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(
                "Invalid customer data",
                validationResult.Errors
            );
        }

        var existing = await _customerRepository.FindByKeyAsync(dto.Key);
        if (existing != null)
        {
            throw new ConflictException($"Customer with key '{dto.Key}' already exists");
        }

        if (dto.ExternalBillingId != null)
        {
            var existingBilling = await _customerRepository.FindByExternalBillingIdAsync(dto.ExternalBillingId);
            if (existingBilling != null)
            {
                throw new ConflictException($"Customer with external billing ID '{dto.ExternalBillingId}' already exists");
            }
        }

        var record = new CustomerRecord
        {
            Id = 0,
            Key = dto.Key,
            DisplayName = dto.DisplayName,
            Email = dto.Email,
            ExternalBillingId = dto.ExternalBillingId,
            Status = CustomerStatus.Active.ToString().ToLowerInvariant(),
            Metadata = dto.Metadata,
            CreatedAt = DateHelper.Now(),
            UpdatedAt = DateHelper.Now()
        };

        var proposed = CustomerMapper.ToDto(CustomerMapper.ToDomain(record));
        proposed = await _hooks.EmitCustomerBeforeAsync(
            HookEvents.CustomerCreatedBefore,
            HookSource.Api,
            null,
            null,
            proposed) ?? proposed;
        ApplyCustomerDtoMutation.Apply(record, proposed, allowKeyChange: true);

        // Re-validate after before-hook mutations
        var revalidateDto = new CreateCustomerDto(
            record.Key,
            record.DisplayName,
            record.Email,
            record.ExternalBillingId,
            record.Metadata);
        var revalidation = await _createValidator.ValidateAsync(revalidateDto);
        if (!revalidation.IsValid)
        {
            throw new ValidationException(
                "Invalid customer data after hook mutation",
                revalidation.Errors
            );
        }

        if (record.Key != dto.Key)
        {
            var keyTaken = await _customerRepository.FindByKeyAsync(record.Key);
            if (keyTaken != null)
            {
                throw new ConflictException($"Customer with key '{record.Key}' already exists");
            }
        }

        if (record.ExternalBillingId != null &&
            record.ExternalBillingId != dto.ExternalBillingId)
        {
            var existingBilling = await _customerRepository.FindByExternalBillingIdAsync(record.ExternalBillingId);
            if (existingBilling != null)
            {
                throw new ConflictException($"Customer with external billing ID '{record.ExternalBillingId}' already exists");
            }
        }

        var savedRecord = await _customerRepository.SaveAsync(record);
        var savedDto = CustomerMapper.ToDto(CustomerMapper.ToDomain(savedRecord));
        await _hooks.EmitCustomerAfterAsync(
            HookEvents.CustomerCreatedAfter,
            HookSource.Api,
            savedRecord.Id,
            null,
            savedDto);
        return savedDto;
    }

    public async Task<CustomerDto> UpdateCustomerAsync(string key, UpdateCustomerDto dto)
    {
        var validationResult = await _updateValidator.ValidateAsync(dto);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(
                "Invalid update data",
                validationResult.Errors
            );
        }

        var record = await _customerRepository.FindByKeyAsync(key);
        if (record == null)
        {
            throw new NotFoundException($"Customer with key '{key}' not found. Please check the customer key and try again.");
        }

        var customer = CustomerMapper.ToDomain(record);
        var oldDto = CustomerMapper.ToDto(customer);

        if (dto.ExternalBillingId != null && dto.ExternalBillingId != customer.Props.ExternalBillingId)
        {
            var existing = await _customerRepository.FindByExternalBillingIdAsync(dto.ExternalBillingId);
            if (existing != null && existing.Id != record.Id)
            {
                throw new ConflictException($"Customer with external billing ID '{dto.ExternalBillingId}' already exists");
            }
        }

        var proposed = new CustomerDto
        {
            Key = oldDto.Key,
            DisplayName = dto.DisplayName ?? oldDto.DisplayName,
            Email = dto.Email ?? oldDto.Email,
            ExternalBillingId = dto.ExternalBillingId ?? oldDto.ExternalBillingId,
            Status = oldDto.Status,
            Metadata = dto.Metadata ?? oldDto.Metadata,
            CreatedAt = oldDto.CreatedAt,
            UpdatedAt = DateHelper.Now().ToUniversalTime().ToString("O")
        };

        proposed = await _hooks.EmitCustomerBeforeAsync(
            HookEvents.CustomerUpdatedBefore,
            HookSource.Api,
            record.Id,
            oldDto,
            proposed) ?? proposed;

        ApplyCustomerDtoMutation.Apply(record, proposed, allowKeyChange: false);
        record.UpdatedAt = DateHelper.Now();

        var savedRecord = await _customerRepository.SaveAsync(record);
        var savedDto = CustomerMapper.ToDto(CustomerMapper.ToDomain(savedRecord));
        await _hooks.EmitCustomerAfterAsync(
            HookEvents.CustomerUpdatedAfter,
            HookSource.Api,
            savedRecord.Id,
            oldDto,
            savedDto);
        return savedDto;
    }

    public async Task<CustomerDto?> GetCustomerAsync(string key)
    {
        var record = await _customerRepository.FindByKeyAsync(key);
        if (record == null) return null;
        
        var customer = CustomerMapper.ToDomain(record);
        return CustomerMapper.ToDto(customer);
    }

    public async Task<List<CustomerDto>> ListCustomersAsync(CustomerFilterDto? filters = null)
    {
        var filterDto = filters ?? new CustomerFilterDto();
        var validationResult = await _filterValidator.ValidateAsync(filterDto);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(
                "Invalid filter parameters",
                validationResult.Errors
            );
        }

        var records = await _customerRepository.FindAllAsync(filterDto);
        return records.Select(r => CustomerMapper.ToDto(CustomerMapper.ToDomain(r))).ToList();
    }

    public async Task ArchiveCustomerAsync(string key)
    {
        var record = await _customerRepository.FindByKeyAsync(key);
        if (record == null)
        {
            throw new NotFoundException($"Customer with key '{key}' not found. Please check the customer key and try again.");
        }

        var oldDto = CustomerMapper.ToDto(CustomerMapper.ToDomain(record));
        var proposed = new CustomerDto
        {
            Key = oldDto.Key,
            DisplayName = oldDto.DisplayName,
            Email = oldDto.Email,
            ExternalBillingId = oldDto.ExternalBillingId,
            Status = CustomerStatus.Archived.ToString().ToLowerInvariant(),
            Metadata = oldDto.Metadata,
            CreatedAt = oldDto.CreatedAt,
            UpdatedAt = DateHelper.Now().ToUniversalTime().ToString("O")
        };

        proposed = await _hooks.EmitCustomerBeforeAsync(
            HookEvents.CustomerArchivedBefore,
            HookSource.Api,
            record.Id,
            oldDto,
            proposed) ?? proposed;

        ApplyCustomerDtoMutation.Apply(record, proposed, allowKeyChange: false);
        record.Status = CustomerStatus.Archived.ToString().ToLowerInvariant();
        record.UpdatedAt = DateHelper.Now();
        var saved = await _customerRepository.SaveAsync(record);
        await _hooks.EmitCustomerAfterAsync(
            HookEvents.CustomerArchivedAfter,
            HookSource.Api,
            saved.Id,
            oldDto,
            CustomerMapper.ToDto(CustomerMapper.ToDomain(saved)));
    }

    public async Task UnarchiveCustomerAsync(string key)
    {
        var record = await _customerRepository.FindByKeyAsync(key);
        if (record == null)
        {
            throw new NotFoundException($"Customer with key '{key}' not found. Please check the customer key and try again.");
        }

        var oldDto = CustomerMapper.ToDto(CustomerMapper.ToDomain(record));
        var proposed = new CustomerDto
        {
            Key = oldDto.Key,
            DisplayName = oldDto.DisplayName,
            Email = oldDto.Email,
            ExternalBillingId = oldDto.ExternalBillingId,
            Status = CustomerStatus.Active.ToString().ToLowerInvariant(),
            Metadata = oldDto.Metadata,
            CreatedAt = oldDto.CreatedAt,
            UpdatedAt = DateHelper.Now().ToUniversalTime().ToString("O")
        };

        proposed = await _hooks.EmitCustomerBeforeAsync(
            HookEvents.CustomerUnarchivedBefore,
            HookSource.Api,
            record.Id,
            oldDto,
            proposed) ?? proposed;

        ApplyCustomerDtoMutation.Apply(record, proposed, allowKeyChange: false);
        record.Status = CustomerStatus.Active.ToString().ToLowerInvariant();
        record.UpdatedAt = DateHelper.Now();
        var saved = await _customerRepository.SaveAsync(record);
        await _hooks.EmitCustomerAfterAsync(
            HookEvents.CustomerUnarchivedAfter,
            HookSource.Api,
            saved.Id,
            oldDto,
            CustomerMapper.ToDto(CustomerMapper.ToDomain(saved)));
    }

    public async Task DeleteCustomerAsync(string key)
    {
        var record = await _customerRepository.FindByKeyAsync(key);
        if (record == null)
        {
            throw new NotFoundException($"Customer with key '{key}' not found. Please check the customer key and try again.");
        }

        var customer = CustomerMapper.ToDomain(record);
        if (!customer.CanDelete())
        {
            throw new DomainException(
                $"Cannot delete customer with status '{customer.Status}'. " +
                "Customer must be archived before permanent deletion."
            );
        }

        var oldDto = CustomerMapper.ToDto(customer);
        await _hooks.EmitCustomerBeforeAsync(
            HookEvents.CustomerDeletedBefore,
            HookSource.Api,
            record.Id,
            oldDto,
            null);

        await _customerRepository.DeleteAsync(record.Id);

        await _hooks.EmitCustomerAfterAsync(
            HookEvents.CustomerDeletedAfter,
            HookSource.Api,
            record.Id,
            oldDto,
            null);
    }
}
