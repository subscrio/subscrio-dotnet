using FluentValidation;
using Subscrio.Core.Application.DTOs;

namespace Subscrio.Core.Application.Validators;

public class UpdateCustomerDtoValidator : AbstractValidator<UpdateCustomerDto>
{
    public UpdateCustomerDtoValidator()
    {
        RuleFor(x => x.DisplayName)
            .MaximumLength(255)
            .When(x => x.DisplayName != null)
            .WithMessage("Display name too long");

        RuleFor(x => x.Email)
            .EmailAddress().WithMessage("Email must be a valid email address")
            .When(x => x.Email != null);

        RuleFor(x => x.ExternalBillingId)
            .MaximumLength(255)
            .When(x => x.ExternalBillingId != null)
            .WithMessage("External billing ID too long");
    }
}


