using System.Linq.Expressions;
using FluentValidation;
using Subscrio.Core.Application.Constants;

namespace Subscrio.Core.Application.Validators;

/// <summary>
/// Shared FluentValidation rules for list filter DTOs.
/// </summary>
public static class FilterValidationRules
{
    public static void IncludeSortOrderRule<T>(
        this AbstractValidator<T> validator,
        Expression<Func<T, string?>> sortOrderSelector,
        bool allowNull = true)
    {
        if (allowNull)
        {
            validator.RuleFor(sortOrderSelector)
                .Must(x => x == null || x == "asc" || x == "desc")
                .WithMessage("SortOrder must be 'asc' or 'desc'");
        }
        else
        {
            validator.RuleFor(sortOrderSelector)
                .Must(x => x == "asc" || x == "desc")
                .WithMessage("SortOrder must be 'asc' or 'desc'");
        }
    }

    public static void IncludeNullablePaginationRules<T>(
        this AbstractValidator<T> validator,
        Expression<Func<T, int?>> limitSelector,
        Expression<Func<T, int?>> offsetSelector)
    {
        validator.RuleFor(limitSelector)
            .Must(x => x is null || (x >= ApplicationConstants.MinPageSize && x <= ApplicationConstants.MaxPageSize))
            .WithMessage($"Limit must be between {ApplicationConstants.MinPageSize} and {ApplicationConstants.MaxPageSize}");

        validator.RuleFor(offsetSelector)
            .Must(x => x is null || x >= 0)
            .WithMessage("Offset must be greater than or equal to 0");
    }

    public static void IncludeRequiredPaginationRules<T>(
        this AbstractValidator<T> validator,
        Expression<Func<T, int>> limitSelector,
        Expression<Func<T, int>> offsetSelector)
    {
        validator.RuleFor(limitSelector)
            .InclusiveBetween(ApplicationConstants.MinPageSize, ApplicationConstants.MaxPageSize)
            .WithMessage($"Limit must be between {ApplicationConstants.MinPageSize} and {ApplicationConstants.MaxPageSize}");

        validator.RuleFor(offsetSelector)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Offset must be greater than or equal to 0");
    }
}
