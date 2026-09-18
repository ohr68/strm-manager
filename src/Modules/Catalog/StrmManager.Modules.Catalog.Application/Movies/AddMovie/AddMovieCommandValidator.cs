using FluentValidation;

namespace StrmManager.Modules.Catalog.Application.Movies.AddMovie;

internal sealed class AddMovieCommandValidator : AbstractValidator<AddMovieCommand>
{
    public AddMovieCommandValidator()
    {
        // The id is interpolated into a provider URL path - reject anything that is not a
        // plain IMDb title id before it can reach the provider. (32 = imdb_id column size.)
        RuleFor(c => c.ImdbId).NotEmpty().MaximumLength(32).Matches(@"^tt\d{1,15}$");
    }
}
