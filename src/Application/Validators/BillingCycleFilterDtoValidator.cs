using FluentValidation;
using Subscrio.Core.Application.DTOs;

namespace Subscrio.Core.Application.Validators;

public class BillingCycleFilterDtoValidator : AbstractValidator<BillingCycleFilterDto>
{
    public BillingCycleFilterDtoValidator()
    {
        RuleFor(x => x.Status)
            .Must(x => x == null || x == "active" || x == "archived")
            .WithMessage("Status must be 'active' or 'archived'")
            .When(x => x.Status != null);

        RuleFor(x => x.DurationUnit)
            .Must(x => x == null || x == "days" || x == "weeks" || x == "months" || x == "years" || x == "forever")
            .WithMessage("DurationUnit must be 'days', 'weeks', 'months', 'years', or 'forever'")
            .When(x => x.DurationUnit != null);

        RuleFor(x => x.SortBy)
            .Must(x => x == null || x == "displayName" || x == "createdAt")
            .WithMessage("SortBy must be 'displayName' or 'createdAt'")
            .When(x => x.SortBy != null);

        this.IncludeSortOrderRule(x => x.SortOrder, allowNull: true);
        this.IncludeRequiredPaginationRules(x => x.Limit, x => x.Offset);
    }
}
