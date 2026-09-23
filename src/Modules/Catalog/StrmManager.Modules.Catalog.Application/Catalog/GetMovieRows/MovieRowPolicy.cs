using StrmManager.Modules.Catalog.Application.Metadata;

namespace StrmManager.Modules.Catalog.Application.Catalog.GetMovieRows;

/// <summary>One provider-neutral row: a stable lowercase id, a display name, and the movies in provider order.</summary>
internal sealed record MovieRow(string Id, string Name, IReadOnlyList<CatalogMovie> Movies);

/// <summary>
/// STRM Manager's own small, fixed, deterministic row policy - not a generic filter framework. Genre rows use only
/// the genre metadata the provider already returns per item (CatalogMovie.Genres, populated by
/// CinemetaCatalogMapper) - no alias table, no inference from title/description, no second provider request.
/// Genre matching is case-insensitive because Cinemeta's own casing is not something this policy should be
/// sensitive to. A movie may legitimately appear in more than one row - each row is an independent view over the
/// same source list, never deduplicated against the others.
/// </summary>
internal static class MovieRowPolicy
{
    private const int MaxMoviesPerRow = 20;

    /// <summary>Order also defines each genre row's stable lowercase id (e.g. "Action" -> "action").</summary>
    private static readonly string[] GenreRowOrder = ["Action", "Comedy", "Drama", "Horror"];

    public static IReadOnlyList<MovieRow> BuildRows(IReadOnlyList<CatalogMovie> movies)
    {
        var rows = new List<MovieRow>
        {
            // Popular is unconditional (never omitted, even if empty) - only genre rows are omitted when empty.
            new("popular", "Popular", movies.Take(MaxMoviesPerRow).ToList()),
        };

        foreach (string genre in GenreRowOrder)
        {
            List<CatalogMovie> matching = movies
                .Where(movie => movie.Genres.Any(movieGenre => string.Equals(movieGenre, genre, StringComparison.OrdinalIgnoreCase)))
                .Take(MaxMoviesPerRow)
                .ToList();

            if (matching.Count == 0)
            {
                continue;
            }

            rows.Add(new MovieRow(genre.ToLowerInvariant(), genre, matching));
        }

        return rows;
    }
}
