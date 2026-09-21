using FluentValidation;

namespace StrmManager.Modules.Catalog.Application.Movies;

/// <summary>
/// The single definition of what a caller-supplied IMDb title id may look like, shared by every movie operation that takes one
/// (AddMovie, GetMovieByImdbId) so they accept and reject exactly the same forms.
/// </summary>
internal static class ImdbIdRules
{
    /// <summary>
    /// A plain, lowercase "tt" + 1-15 digits. The id is interpolated into a provider URL path and compared to a stored value, so anything
    /// else is rejected before it can reach either. (32 = the imdb_id column size.)
    /// </summary>
    public static IRuleBuilderOptions<T, string> MustBeAnImdbTitleId<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MaximumLength(32).Matches(@"^tt\d{1,15}$");
}
