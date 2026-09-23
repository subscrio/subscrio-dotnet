using FluentValidation;
using Subscrio.Core.Application.DTOs;

namespace Subscrio.Core.Application.Validators;

public class FeatureFilterDtoValidator : AbstractValidator<FeatureFilterDto>
{
    public FeatureFilterDtoValidator()
    {
        RuleFor(x => x.Status)
            .Must(x => x == null || x == "active" || x == "archived")
            .WithMessage("Status must be 'active' or 'archived'")
            .When(x => x.Status != null);

        RuleFor(x => x.ValueType)
            .Must(x => x == null || x == "toggle" || x == "numeric" || x == "text" || x == "metered")
            .WithMessage("ValueType must be 'toggle', 'numeric', or 'text'")
            .When(x => x.ValueType != null);

        RuleFor(x => x.SortBy)
            .Must(x => x == null || x == "displayName" || x == "createdAt")
            .WithMessage("SortBy must be 'displayName' or 'createdAt'")
            .When(x => x.SortBy != null);

        this.IncludeSortOrderRule(x => x.SortOrder, allowNull: true);
        this.IncludeRequiredPaginationRules(x => x.Limit, x => x.Offset);
    }
}
