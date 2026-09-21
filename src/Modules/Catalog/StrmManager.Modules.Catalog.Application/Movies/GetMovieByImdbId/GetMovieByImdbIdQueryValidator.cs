using FluentValidation;

namespace StrmManager.Modules.Catalog.Application.Movies.GetMovieByImdbId;

internal sealed class GetMovieByImdbIdQueryValidator : AbstractValidator<GetMovieByImdbIdQuery>
{
    public GetMovieByImdbIdQueryValidator()
    {
        // The same rule AddMovie applies (one shared definition), so both accept and reject exactly the same IMDb forms.
        RuleFor(q => q.ImdbId).MustBeAnImdbTitleId();
    }
}
