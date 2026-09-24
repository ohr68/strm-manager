using FluentValidation;
using StrmManager.Modules.Catalog.Application.Movies;

namespace StrmManager.Modules.Catalog.Application.Catalog.GetMovieDetail;

/// <summary>The same shared IMDb id rule AddMovie/GetMovieByImdbId use, so this endpoint accepts and rejects exactly the same forms.</summary>
internal sealed class GetMovieDetailQueryValidator : AbstractValidator<GetMovieDetailQuery>
{
    public GetMovieDetailQueryValidator()
    {
        RuleFor(q => q.ImdbId).MustBeAnImdbTitleId();
    }
}
