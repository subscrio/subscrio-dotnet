using FluentValidation;
using Subscrio.Core.Application.DTOs;

namespace Subscrio.Core.Application.Validators;

public class PlanFilterDtoValidator : AbstractValidator<PlanFilterDto>
{
    public PlanFilterDtoValidator()
    {
        RuleFor(x => x.Status)
            .Must(x => x == null || x == "active" || x == "archived")
            .WithMessage("Status must be 'active' or 'archived'")
            .When(x => x.Status != null);

        RuleFor(x => x.SortBy)
            .Must(x => x == null || x == "displayName" || x == "createdAt")
            .WithMessage("SortBy must be 'displayName' or 'createdAt'")
            .When(x => x.SortBy != null);

        this.IncludeSortOrderRule(x => x.SortOrder, allowNull: true);
        this.IncludeRequiredPaginationRules(x => x.Limit, x => x.Offset);
    }
}
