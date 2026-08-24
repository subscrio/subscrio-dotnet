using FluentValidation;
using Subscrio.Core.Application.Constants;
using Subscrio.Core.Application.DTOs;

namespace Subscrio.Core.Application.Validators;

public class CreateProductDtoValidator : AbstractValidator<CreateProductDto>
{
    public CreateProductDtoValidator()
    {
        RuleFor(x => x.Key)
            .NotEmpty().WithMessage("Key is required")
            .MinimumLength(ApplicationConstants.MinKeyLength).WithMessage("Key is required")
            .MaximumLength(ApplicationConstants.MaxKeyLength).WithMessage($"Key too long (max {ApplicationConstants.MaxKeyLength} characters)")
            .Matches(@"^[a-z0-9-]+$").WithMessage("Key must be lowercase alphanumeric with hyphens");

        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("Display name is required")
            .MinimumLength(ApplicationConstants.MinDisplayNameLength).WithMessage("Display name is required")
            .MaximumLength(ApplicationConstants.MaxDisplayNameLength).WithMessage($"Display name too long (max {ApplicationConstants.MaxDisplayNameLength} characters)");

        RuleFor(x => x.Description)
            .MaximumLength(ApplicationConstants.MaxDescriptionLength)
            .When(x => x.Description != null)
            .WithMessage($"Description too long (max {ApplicationConstants.MaxDescriptionLength} characters)");
    }
}


