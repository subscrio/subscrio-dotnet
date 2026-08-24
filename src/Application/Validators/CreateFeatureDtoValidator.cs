using FluentValidation;
using Subscrio.Core.Application.Constants;
using Subscrio.Core.Application.DTOs;

namespace Subscrio.Core.Application.Validators;

public class CreateFeatureDtoValidator : AbstractValidator<CreateFeatureDto>
{
    public CreateFeatureDtoValidator()
    {
        RuleFor(x => x.Key)
            .NotEmpty().WithMessage("Key is required")
            .MinimumLength(1).WithMessage("Key is required")
            .MaximumLength(255).WithMessage("Key too long")
            .Matches(@"^[a-zA-Z0-9-_]+$").WithMessage("Key must be alphanumeric with hyphens/underscores");

        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("Display name is required")
            .MinimumLength(1).WithMessage("Display name is required")
            .MaximumLength(255).WithMessage("Display name too long");

        RuleFor(x => x.Description)
            .MaximumLength(1000)
            .When(x => x.Description != null)
            .WithMessage("Description too long");

        RuleFor(x => x.ValueType)
            .Must(x => x == "toggle" || x == "numeric" || x == "text")
            .WithMessage("ValueType must be 'toggle', 'numeric', or 'text'");

        RuleFor(x => x.DefaultValue)
            .NotEmpty().WithMessage("Default value is required")
            .MinimumLength(1).WithMessage("Default value is required");

        RuleFor(x => x.GroupName)
            .MaximumLength(255)
            .When(x => x.GroupName != null)
            .WithMessage("Group name too long");

        // Custom validation: defaultValue must match valueType
        RuleFor(x => x)
            .Must(x =>
            {
                if (x.ValueType == "toggle")
                {
                    return x.DefaultValue == "true" || x.DefaultValue == "false";
                }
                if (x.ValueType == "numeric")
                {
                    return double.TryParse(x.DefaultValue, out var num) && double.IsFinite(num);
                }
                // Text type accepts any string
                return true;
            })
            .WithMessage("Invalid default value for the selected value type. Toggle must be \"true\" or \"false\", Numeric must be a valid number.")
            .OverridePropertyName("DefaultValue");
    }
}


