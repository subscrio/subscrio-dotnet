using FluentValidation;
using Subscrio.Core.Application.DTOs;

namespace Subscrio.Core.Application.Validators;

public class CreatePlanDtoValidator : AbstractValidator<CreatePlanDto>
{
    public CreatePlanDtoValidator()
    {
        RuleFor(x => x.ProductKey)
            .NotEmpty().WithMessage("Product key is required")
            .MinimumLength(1).WithMessage("Product key is required")
            .Matches(@"^[a-z0-9-]+$").WithMessage("Product key must be lowercase alphanumeric with hyphens");

        RuleFor(x => x.Key)
            .NotEmpty().WithMessage("Key is required")
            .MinimumLength(1).WithMessage("Key is required")
            .MaximumLength(255).WithMessage("Key too long")
            .Matches(@"^[a-z0-9-]+$").WithMessage("Key must be globally unique across all plans");

        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("Display name is required")
            .MinimumLength(1).WithMessage("Display name is required")
            .MaximumLength(255).WithMessage("Display name too long");

        RuleFor(x => x.Description)
            .MaximumLength(1000)
            .When(x => x.Description != null)
            .WithMessage("Description too long");
    }
}


