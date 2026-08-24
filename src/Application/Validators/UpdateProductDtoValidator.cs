using FluentValidation;
using Subscrio.Core.Application.Constants;
using Subscrio.Core.Application.DTOs;

namespace Subscrio.Core.Application.Validators;

public class UpdateProductDtoValidator : AbstractValidator<UpdateProductDto>
{
    public UpdateProductDtoValidator()
    {
        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("Display name is required")
            .MinimumLength(ApplicationConstants.MinDisplayNameLength).WithMessage("Display name is required")
            .MaximumLength(ApplicationConstants.MaxDisplayNameLength).WithMessage($"Display name too long (max {ApplicationConstants.MaxDisplayNameLength} characters)")
            .When(x => x.DisplayName != null);

        RuleFor(x => x.Description)
            .MaximumLength(ApplicationConstants.MaxDescriptionLength)
            .When(x => x.Description != null)
            .WithMessage($"Description too long (max {ApplicationConstants.MaxDescriptionLength} characters)");
    }
}


