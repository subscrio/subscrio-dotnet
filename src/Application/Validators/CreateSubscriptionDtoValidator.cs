using FluentValidation;
using Subscrio.Core.Application.DTOs;

namespace Subscrio.Core.Application.Validators;

public class CreateSubscriptionDtoValidator : AbstractValidator<CreateSubscriptionDto>
{
    public CreateSubscriptionDtoValidator()
    {
        RuleFor(x => x.Key)
            .NotEmpty().WithMessage("Subscription key is required")
            .MinimumLength(1).WithMessage("Subscription key is required")
            .MaximumLength(255).WithMessage("Subscription key too long")
            .Matches(@"^[a-zA-Z0-9-_]+$").WithMessage("Subscription key must be alphanumeric with hyphens/underscores");

        RuleFor(x => x.CustomerKey)
            .NotEmpty().WithMessage("Customer key is required")
            .MinimumLength(1).WithMessage("Customer key is required");

        RuleFor(x => x.BillingCycleKey)
            .NotEmpty().WithMessage("Billing cycle key is required")
            .MinimumLength(1).WithMessage("Billing cycle key is required")
            .Matches(@"^[a-z0-9-]+$").WithMessage("Billing cycle key must be lowercase alphanumeric with hyphens");
    }
}


