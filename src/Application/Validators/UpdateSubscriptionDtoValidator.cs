using FluentValidation;
using Subscrio.Core.Application.DTOs;

namespace Subscrio.Core.Application.Validators;

public class UpdateSubscriptionDtoValidator : AbstractValidator<UpdateSubscriptionDto>
{
    public UpdateSubscriptionDtoValidator()
    {
        RuleFor(x => x.BillingCycleKey)
            .NotEmpty().WithMessage("Billing cycle key is required")
            .MinimumLength(1).WithMessage("Billing cycle key is required")
            .Matches(@"^[a-z0-9-]+$").WithMessage("Billing cycle key must be lowercase alphanumeric with hyphens")
            .When(x => x.BillingCycleKey != null);
    }
}


