using FluentValidation;

namespace StrmManager.Modules.Catalog.Application.Catalog.SearchMovies;

/// <summary>
/// The endpoint trims the raw query string before constructing SearchMoviesQuery, so this validates the
/// already-trimmed value: required (rejects empty/whitespace-only), and bounded to 100 characters - never silently
/// truncated, a longer query is a 400.
/// </summary>
internal sealed class SearchMoviesQueryValidator : AbstractValidator<SearchMoviesQuery>
{
    public SearchMoviesQueryValidator()
    {
        RuleFor(q => q.Query)
            .NotEmpty().WithMessage("query is required.")
            .MaximumLength(100).WithMessage("query must be at most 100 characters.");
    }
}
