using FluentValidation;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Application.Utils;

namespace Subscrio.Core.Application.Validators;

public class UpdateFeatureDtoValidator : AbstractValidator<UpdateFeatureDto>
{
    public UpdateFeatureDtoValidator()
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

        RuleFor(x => x.ValueType)
            .Must(x => x == null || x == "toggle" || x == "numeric" || x == "text" || x == "metered")
            .WithMessage("ValueType must be 'toggle', 'numeric', or 'text'")
            .When(x => x.ValueType != null);

        RuleFor(x => x.DefaultValue)
            .NotEmpty().WithMessage("Default value is required")
            .MinimumLength(1).WithMessage("Default value is required")
            .When(x => x.DefaultValue != null);

        RuleFor(x => x.GroupName)
            .MaximumLength(255)
            .When(x => x.GroupName != null)
            .WithMessage("Group name too long");

        RuleFor(x => x)
            .Must(x => FeatureValueValidator.TryValidate(x.DefaultValue!, x.ValueType!, out _))
            .WithMessage("Invalid default value for the selected value type. Toggle must be \"true\" or \"false\", Numeric must be a valid number.")
            .OverridePropertyName("DefaultValue")
            .When(x => x.ValueType != null && x.DefaultValue != null);
    }
}
