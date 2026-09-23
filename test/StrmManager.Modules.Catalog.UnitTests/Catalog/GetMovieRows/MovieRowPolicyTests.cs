using StrmManager.Modules.Catalog.Application.Catalog.GetMovieRows;
using StrmManager.Modules.Catalog.Application.Metadata;

namespace StrmManager.Modules.Catalog.UnitTests.Catalog.GetMovieRows;

/// <summary>
/// Focused unit tests for MovieRowPolicy - the only real logic UI-4a's rows endpoint adds. Genre mapping itself
/// (Cinemeta -> CatalogMovie.Genres) is covered in CinemetaCatalogProviderTests, not duplicated here.
/// </summary>
public sealed class MovieRowPolicyTests
{
    private static CatalogMovie Movie(string id, params string[] genres) => new(id, id, 2026, null, genres);

    [Fact]
    public void BuildRows_OrdersRowsPopularThenActionCommaDramaHorror_OmittingEmptyGenreRows()
    {
        // No Action, no Horror movies anywhere in the source list.
        var movies = new List<CatalogMovie> { Movie("tt1", "Comedy"), Movie("tt2", "Drama") };

        IReadOnlyList<MovieRow> rows = MovieRowPolicy.BuildRows(movies);

        Assert.Collection(
            rows,
            row => Assert.Equal("popular", row.Id),
            row => Assert.Equal("comedy", row.Id),
            row => Assert.Equal("drama", row.Id));
    }

    [Fact]
    public void BuildRows_GenreMatchingIsCaseInsensitive()
    {
        var movies = new List<CatalogMovie> { Movie("tt1", "action"), Movie("tt2", "ACTION") };

        IReadOnlyList<MovieRow> rows = MovieRowPolicy.BuildRows(movies);

        MovieRow actionRow = Assert.Single(rows, row => row.Id == "action");
        Assert.Equal(2, actionRow.Movies.Count);
    }

    [Fact]
    public void BuildRows_PreservesOriginalProviderOrderWithinARow()
    {
        var movies = new List<CatalogMovie> { Movie("tt3", "Drama"), Movie("tt1", "Drama"), Movie("tt2", "Drama") };

        IReadOnlyList<MovieRow> rows = MovieRowPolicy.BuildRows(movies);

        MovieRow dramaRow = Assert.Single(rows, row => row.Id == "drama");
        Assert.Equal(["tt3", "tt1", "tt2"], dramaRow.Movies.Select(movie => movie.ExternalId));
    }

    [Fact]
    public void BuildRows_CapsEachRowAtTwentyMovies()
    {
        List<CatalogMovie> movies = Enumerable.Range(1, 25).Select(i => Movie($"tt{i}", "Horror")).ToList();

        IReadOnlyList<MovieRow> rows = MovieRowPolicy.BuildRows(movies);

        MovieRow horrorRow = Assert.Single(rows, row => row.Id == "horror");
        Assert.Equal(20, horrorRow.Movies.Count);
        Assert.Equal("tt1", horrorRow.Movies[0].ExternalId);
        Assert.Equal("tt20", horrorRow.Movies[^1].ExternalId);
    }

    [Fact]
    public void BuildRows_AMovieMayAppearInMultipleGenreRows_WithoutDeduplication()
    {
        var movies = new List<CatalogMovie> { Movie("tt1", "Action", "Comedy") };

        IReadOnlyList<MovieRow> rows = MovieRowPolicy.BuildRows(movies);

        Assert.Contains(rows, row => row.Id == "action" && row.Movies.Any(movie => movie.ExternalId == "tt1"));
        Assert.Contains(rows, row => row.Id == "comedy" && row.Movies.Any(movie => movie.ExternalId == "tt1"));
    }

    [Fact]
    public void BuildRows_PopularRowIsAlwaysPresent_EvenWhenSourceIsEmpty()
    {
        IReadOnlyList<MovieRow> rows = MovieRowPolicy.BuildRows([]);

        MovieRow popular = Assert.Single(rows);
        Assert.Equal("popular", popular.Id);
        Assert.Empty(popular.Movies);
    }
}
