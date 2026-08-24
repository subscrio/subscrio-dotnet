using FluentValidation;
using Subscrio.Core.Application.DTOs;

namespace Subscrio.Core.Application.Validators;

public class UpdateBillingCycleDtoValidator : AbstractValidator<UpdateBillingCycleDto>
{
    public UpdateBillingCycleDtoValidator()
    {
        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("Display name is required")
            .MinimumLength(1).WithMessage("Display name is required")
            .MaximumLength(255).WithMessage("Display name too long")
            .When(x => x.DisplayName != null);

        RuleFor(x => x.Description)
            .MaximumLength(1000)
            .When(x => x.Description != null)
            .WithMessage("Description too long");

        RuleFor(x => x.DurationValue)
            .GreaterThan(0).WithMessage("Duration value must be positive")
            .When(x => x.DurationValue.HasValue);

        RuleFor(x => x.DurationUnit)
            .Must(x => x == null || x == "days" || x == "weeks" || x == "months" || x == "years" || x == "forever")
            .WithMessage("DurationUnit must be 'days', 'weeks', 'months', 'years', or 'forever'")
            .When(x => x.DurationUnit != null);

        RuleFor(x => x.ExternalProductId)
            .MaximumLength(255)
            .When(x => x.ExternalProductId != null)
            .WithMessage("External product ID too long");

        // Custom validation: Only validate if durationUnit is provided
        RuleFor(x => x)
            .Must(x =>
            {
                if (x.DurationUnit == null)
                {
                    return true; // If durationUnit is not provided, durationValue can be provided or not
                }
                if (x.DurationUnit == "forever")
                {
                    return x.DurationValue == null; // Can't have durationValue with forever
                }
                return x.DurationValue != null; // Must have durationValue for non-forever
            })
            .WithMessage("Duration value is required for non-forever durations, and must be undefined for forever duration")
            .OverridePropertyName("DurationValue")
            .When(x => x.DurationUnit != null);
    }
}


