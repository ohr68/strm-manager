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

    private sealed class FakeStrmManagerClient(CatalogMoviesResult? popularResult = null, MovieRowsResult? rowsResult = null) : IStrmManagerClient
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
    }
}
