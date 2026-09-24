using Jellyfin.Plugin.StrmManager.Api;
using Jellyfin.Plugin.StrmManager.StrmManagerClient;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.StrmManager.Tests;

/// <summary>
/// Focused unit tests for CatalogController.GetPopularMovies/GetMovieRows - calls the action methods directly on a
/// plainly constructed controller, matching MoviesControllerTests' style (no TestServer/running host).
/// </summary>
public sealed class CatalogControllerTests
{
    [Fact]
    public async Task GetPopularMovies_Found_Returns200WithLowercaseMovieFields()
    {
        var movies = new List<CatalogMovie> { new("tt1", "A Movie", 2026, "https://example.test/poster.jpg") };
        var controller = new CatalogController(new FakeStrmManagerClient(popularResult: new CatalogMoviesResult.Found(movies)));

        IActionResult result = await controller.GetPopularMovies(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, ok.StatusCode);
        object? moviesValue = ok.Value!.GetType().GetProperty("movies")!.GetValue(ok.Value);
        Assert.NotNull(moviesValue);
    }

    [Fact]
    public async Task GetPopularMovies_Unreachable_Returns503()
    {
        var controller = new CatalogController(new FakeStrmManagerClient(popularResult: new CatalogMoviesResult.Unreachable("Connection refused")));

        IActionResult result = await controller.GetPopularMovies(CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetPopularMovies_Error_Returns502()
    {
        var controller = new CatalogController(new FakeStrmManagerClient(popularResult: new CatalogMoviesResult.Error("Unexpected HTTP status 500.")));

        IActionResult result = await controller.GetPopularMovies(CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(502, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetMovieRows_Found_Returns200WithLowercaseRowsField()
    {
        var rows = new List<MovieRow>
        {
            new("popular", "Popular", [new CatalogMovie("tt1", "A Movie", 2026, "https://example.test/poster.jpg")]),
        };
        var controller = new CatalogController(new FakeStrmManagerClient(rowsResult: new MovieRowsResult.Found(rows)));

        IActionResult result = await controller.GetMovieRows(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, ok.StatusCode);
        object? rowsValue = ok.Value!.GetType().GetProperty("rows")!.GetValue(ok.Value);
        Assert.NotNull(rowsValue);
    }

    [Fact]
    public async Task GetMovieRows_Unreachable_Returns503()
    {
        var controller = new CatalogController(new FakeStrmManagerClient(rowsResult: new MovieRowsResult.Unreachable("Connection refused")));

        IActionResult result = await controller.GetMovieRows(CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetMovieRows_Error_Returns502()
    {
        var controller = new CatalogController(new FakeStrmManagerClient(rowsResult: new MovieRowsResult.Error("Unexpected HTTP status 500.")));

        IActionResult result = await controller.GetMovieRows(CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(502, objectResult.StatusCode);
    }

    [Fact]
    public async Task SearchMovies_Found_Returns200WithLowercaseMoviesField()
    {
        var movies = new List<CatalogMovie> { new("tt0468569", "The Dark Knight", 2008, "https://example.test/poster.jpg") };
        var controller = new CatalogController(new FakeStrmManagerClient(searchResult: new SearchMoviesResult.Found(movies)));

        IActionResult result = await controller.SearchMovies("The Dark Knight", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, ok.StatusCode);
        object? moviesValue = ok.Value!.GetType().GetProperty("movies")!.GetValue(ok.Value);
        Assert.NotNull(moviesValue);
    }

    [Fact]
    public async Task SearchMovies_EmptyFound_Returns200WithEmptyMoviesArray_NotAFailure()
    {
        var controller = new CatalogController(new FakeStrmManagerClient(searchResult: new SearchMoviesResult.Found([])));

        IActionResult result = await controller.SearchMovies("zzzzstrmmanagerthismoviedoesnotexist99999", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, ok.StatusCode);
        var movies = (IEnumerable<object>)ok.Value!.GetType().GetProperty("movies")!.GetValue(ok.Value)!;
        Assert.Empty(movies);
    }

    [Fact]
    public async Task SearchMovies_Invalid_Returns400()
    {
        var controller = new CatalogController(new FakeStrmManagerClient(searchResult: new SearchMoviesResult.Invalid()));

        IActionResult result = await controller.SearchMovies(string.Empty, CancellationToken.None);

        Assert.IsType<BadRequestResult>(result);
    }

    [Fact]
    public async Task SearchMovies_Unreachable_Returns503()
    {
        var controller = new CatalogController(new FakeStrmManagerClient(searchResult: new SearchMoviesResult.Unreachable("Connection refused")));

        IActionResult result = await controller.SearchMovies("The Dark Knight", CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, objectResult.StatusCode);
    }

    [Fact]
    public async Task SearchMovies_Error_Returns502()
    {
        var controller = new CatalogController(new FakeStrmManagerClient(searchResult: new SearchMoviesResult.Error("Unexpected HTTP status 500.")));

        IActionResult result = await controller.SearchMovies("The Dark Knight", CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(502, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetMovieDetail_Found_Returns200WithLowercaseFields()
    {
        var detail = new MovieDetail("tt0468569", "The Dark Knight", 2008, 152, "...", ["Action", "Crime", "Drama"], "9.1", "https://example.test/poster.jpg", "https://example.test/backdrop.jpg");
        var controller = new CatalogController(new FakeStrmManagerClient(detailResult: new MovieDetailResult.Found(detail)));

        IActionResult result = await controller.GetMovieDetail("tt0468569", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, ok.StatusCode);
        Assert.Equal("tt0468569", ok.Value!.GetType().GetProperty("externalId")!.GetValue(ok.Value));
        Assert.Equal("The Dark Knight", ok.Value.GetType().GetProperty("title")!.GetValue(ok.Value));
    }

    [Fact]
    public async Task GetMovieDetail_NotFound_Returns404()
    {
        var controller = new CatalogController(new FakeStrmManagerClient(detailResult: new MovieDetailResult.NotFound()));

        IActionResult result = await controller.GetMovieDetail("tt0000000", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetMovieDetail_Invalid_Returns400()
    {
        var controller = new CatalogController(new FakeStrmManagerClient(detailResult: new MovieDetailResult.Invalid()));

        IActionResult result = await controller.GetMovieDetail("not-an-imdb-id", CancellationToken.None);

        Assert.IsType<BadRequestResult>(result);
    }

    [Fact]
    public async Task GetMovieDetail_Unreachable_Returns503()
    {
        var controller = new CatalogController(new FakeStrmManagerClient(detailResult: new MovieDetailResult.Unreachable("Connection refused")));

        IActionResult result = await controller.GetMovieDetail("tt0468569", CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetMovieDetail_Error_Returns502()
    {
        var controller = new CatalogController(new FakeStrmManagerClient(detailResult: new MovieDetailResult.Error("Unexpected HTTP status 500.")));

        IActionResult result = await controller.GetMovieDetail("tt0468569", CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(502, objectResult.StatusCode);
    }

    private sealed class FakeStrmManagerClient(
        CatalogMoviesResult? popularResult = null,
        MovieRowsResult? rowsResult = null,
        SearchMoviesResult? searchResult = null,
        MovieDetailResult? detailResult = null) : IStrmManagerClient
    {
        public Task<MovieLookupResult> GetByImdbIdAsync(string imdbId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("CatalogController must not call GetByImdbIdAsync.");

        public Task<AddMovieResult> AddMovieAsync(string imdbId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("CatalogController must not call AddMovieAsync.");

        public Task<ProcessMovieResult> ProcessMovieAsync(Guid movieId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("CatalogController must not call ProcessMovieAsync.");

        public Task<CatalogMoviesResult> GetPopularMoviesAsync(CancellationToken cancellationToken) =>
            popularResult is not null
                ? Task.FromResult(popularResult)
                : throw new NotSupportedException("GetMovieRows must not call GetPopularMoviesAsync.");

        public Task<MovieRowsResult> GetMovieRowsAsync(CancellationToken cancellationToken) =>
            rowsResult is not null
                ? Task.FromResult(rowsResult)
                : throw new NotSupportedException("GetPopularMovies must not call GetMovieRowsAsync.");

        public Task<SearchMoviesResult> SearchMoviesAsync(string query, CancellationToken cancellationToken) =>
            searchResult is not null
                ? Task.FromResult(searchResult)
                : throw new NotSupportedException("Only SearchMovies should call SearchMoviesAsync.");

        public Task<MovieDetailResult> GetMovieDetailAsync(string imdbId, CancellationToken cancellationToken) =>
            detailResult is not null
                ? Task.FromResult(detailResult)
                : throw new NotSupportedException("Only GetMovieDetail should call GetMovieDetailAsync.");
    }
}
