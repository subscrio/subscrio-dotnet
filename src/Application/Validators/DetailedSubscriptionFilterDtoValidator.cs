using FluentValidation;
using Subscrio.Core.Application.DTOs;

namespace Subscrio.Core.Application.Validators;

public class DetailedSubscriptionFilterDtoValidator : AbstractValidator<DetailedSubscriptionFilterDto>
{
    public DetailedSubscriptionFilterDtoValidator()
    {
        RuleFor(x => x.Status)
            .Must(x => x == null || x == "pending" || x == "active" || x == "trial" || x == "cancelled" || x == "cancellation_pending" || x == "expired")
            .WithMessage("Status must be 'pending', 'active', 'trial', 'cancelled', 'cancellation_pending', or 'expired'")
            .When(x => x.Status != null);

        RuleFor(x => x.SortBy)
            .Must(x => x == null || x == "activationDate" || x == "expirationDate" || x == "createdAt" || x == "updatedAt" || x == "currentPeriodStart" || x == "currentPeriodEnd")
            .WithMessage("SortBy must be 'activationDate', 'expirationDate', 'createdAt', 'updatedAt', 'currentPeriodStart', or 'currentPeriodEnd'")
            .When(x => x.SortBy != null);

        this.IncludeSortOrderRule(x => x.SortOrder, allowNull: true);
        this.IncludeNullablePaginationRules(x => x.Limit, x => x.Offset);
    }
}
