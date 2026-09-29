using FluentValidation;

namespace StrmManager.Modules.Catalog.Application.Catalog.SearchSeries;

internal sealed class SearchSeriesQueryValidator
    : AbstractValidator<SearchSeriesQuery>
{
    public SearchSeriesQueryValidator()
    {
        RuleFor(query => query.Query)
            .NotEmpty()
            .WithMessage("query is required.")
            .MaximumLength(100)
            .WithMessage(
                "query must be at most 100 characters.");
    }
}
