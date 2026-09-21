using FluentValidation;

namespace StrmManager.Modules.Catalog.Application.Movies.AddMovie;

internal sealed class AddMovieCommandValidator : AbstractValidator<AddMovieCommand>
{
    public AddMovieCommandValidator()
    {
        // The id is interpolated into a provider URL path - reject anything that is not a
        // plain IMDb title id before it can reach the provider (see ImdbIdRules).
        RuleFor(c => c.ImdbId).MustBeAnImdbTitleId();
    }
}
