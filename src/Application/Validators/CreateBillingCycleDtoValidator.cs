using FluentValidation;
using Subscrio.Core.Application.DTOs;

namespace Subscrio.Core.Application.Validators;

public class CreateBillingCycleDtoValidator : AbstractValidator<CreateBillingCycleDto>
{
    public CreateBillingCycleDtoValidator()
    {
        RuleFor(x => x.PlanKey)
            .NotEmpty().WithMessage("Plan key is required")
            .MinimumLength(1).WithMessage("Plan key is required")
            .Matches(@"^[a-z0-9-]+$").WithMessage("Plan key must be lowercase alphanumeric with hyphens");

        RuleFor(x => x.Key)
            .NotEmpty().WithMessage("Key is required")
            .MinimumLength(1).WithMessage("Key is required")
            .MaximumLength(255).WithMessage("Key too long")
            .Matches(@"^[a-z0-9-]+$").WithMessage("Key must be globally unique across all billing cycles");

        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("Display name is required")
            .MinimumLength(1).WithMessage("Display name is required")
            .MaximumLength(255).WithMessage("Display name too long");

        RuleFor(x => x.Description)
            .MaximumLength(1000)
            .When(x => x.Description != null)
            .WithMessage("Description too long");

        RuleFor(x => x.DurationValue)
            .GreaterThan(0).WithMessage("Duration value must be positive")
            .When(x => x.DurationValue.HasValue);

        RuleFor(x => x.DurationUnit)
            .Must(x => x == "days" || x == "weeks" || x == "months" || x == "years" || x == "forever")
            .WithMessage("DurationUnit must be 'days', 'weeks', 'months', 'years', or 'forever'");

        RuleFor(x => x.ExternalProductId)
            .MaximumLength(255)
            .When(x => x.ExternalProductId != null)
            .WithMessage("External product ID too long");

        // Custom validation: If durationUnit is 'forever', durationValue should be undefined
        // If durationUnit is not 'forever', durationValue should be provided
        RuleFor(x => x)
            .Must(x =>
            {
                if (x.DurationUnit == "forever")
                {
                    return x.DurationValue == null;
                }
                return x.DurationValue != null;
            })
            .WithMessage("Duration value is required for non-forever durations, and must be undefined for forever duration")
            .OverridePropertyName("DurationValue");
    }
}


