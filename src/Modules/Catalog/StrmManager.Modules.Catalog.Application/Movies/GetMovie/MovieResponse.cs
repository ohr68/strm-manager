using StrmManager.Modules.Catalog.Domain.Movies;

namespace StrmManager.Modules.Catalog.Application.Movies.GetMovie;

public sealed record MovieResponse(
    Guid Id,
    string? ImdbId,
    string? TmdbId,
    string? TvdbId,
    string Title,
    int Year,
    TimeSpan? Runtime,
    DateTime ReleaseAtUtc,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc)
{
    /// <summary>
    /// The one Movie -> MovieResponse mapping, shared by every movie lookup (by id and by IMDb id) so their representations of the same
    /// movie cannot drift apart.
    /// </summary>
    public static MovieResponse From(Movie movie) => new(
        movie.Id,
        movie.ExternalIds.ImdbId,
        movie.ExternalIds.TmdbId,
        movie.ExternalIds.TvdbId,
        movie.Title,
        movie.Year,
        movie.Runtime,
        movie.ReleaseAtUtc,
        movie.Status.ToString(),
        movie.CreatedAtUtc,
        movie.UpdatedAtUtc);
}
