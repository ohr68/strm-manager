using Jellyfin.Plugin.StrmManager.Api;
using Jellyfin.Plugin.StrmManager.Movies;
using Jellyfin.Plugin.StrmManager.Processing;
using Jellyfin.Plugin.StrmManager.StrmManagerClient;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.StrmManager.Tests;

/// <summary>
/// Focused unit tests for MoviesController.ProcessMovie - the P5 producer action. Calls the action method directly
/// on a plainly-constructed controller (no TestServer/running host, matching this plugin's established testing
/// style); routing and the class-level [Authorize] attribute are declarative metadata that this style of test
/// never exercises and doesn't need to - see the producer discovery report. IStrmManagerClient/IEnsureMovieService
/// are unused by this action and fail loudly if ever called.
/// </summary>
public sealed class MoviesControllerTests
{
    private static readonly Guid MovieId = Guid.Parse("1393e862-e5f1-4fe6-b4a0-b3bb8ee0daa6");

    private static MoviesController CreateController(ProcessMovieEnqueueResult result, out FakeProcessMovieQueue queue)
    {
        queue = new FakeProcessMovieQueue(result);
        return new MoviesController(
            new UnusedStrmManagerClient(),
            new UnusedEnsureMovieService(),
            queue,
            NullLogger<MoviesController>.Instance);
    }

    [Fact]
    public void ProcessMovie_QueueAccepted_Returns202WithExpectedBody()
    {
        MoviesController controller = CreateController(ProcessMovieEnqueueResult.Accepted, out FakeProcessMovieQueue queue);

        IActionResult result = controller.ProcessMovie(MovieId);

        var accepted = Assert.IsType<AcceptedResult>(result);
        Assert.Equal(StatusCodes.Status202Accepted, accepted.StatusCode);
        AssertBody(accepted.Value, MovieId, "Accepted");
        Assert.Equal(MovieId, queue.RequestedMovieId);
    }

    [Fact]
    public void ProcessMovie_QueueAlreadyQueued_Returns409WithExpectedBody()
    {
        MoviesController controller = CreateController(ProcessMovieEnqueueResult.AlreadyQueued, out FakeProcessMovieQueue queue);

        IActionResult result = controller.ProcessMovie(MovieId);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        AssertBody(conflict.Value, MovieId, "AlreadyQueued");
        Assert.Equal(MovieId, queue.RequestedMovieId);
    }

    [Fact]
    public void ProcessMovie_QueueFull_Returns429WithExpectedBody()
    {
        MoviesController controller = CreateController(ProcessMovieEnqueueResult.Full, out FakeProcessMovieQueue queue);

        IActionResult result = controller.ProcessMovie(MovieId);

        // ObjectResult is the direct return type of StatusCode(code, value) - checked here without a TestServer/HTTP
        // harness, so there is no response-header surface to assert "no Retry-After" against; the source contract
        // (no header-setting code exists in the action) is the proof, per the producer report.
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status429TooManyRequests, objectResult.StatusCode);
        AssertBody(objectResult.Value, MovieId, "Full");
        Assert.Equal(MovieId, queue.RequestedMovieId);
    }

    private static void AssertBody(object? value, Guid expectedMovieId, string expectedQueueStatus)
    {
        Assert.NotNull(value);
        Guid movieId = (Guid)value.GetType().GetProperty("movieId")!.GetValue(value)!;
        var queueStatus = (string)value.GetType().GetProperty("queueStatus")!.GetValue(value)!;
        Assert.Equal(expectedMovieId, movieId);
        Assert.Equal(expectedQueueStatus, queueStatus);
    }

    private sealed class FakeProcessMovieQueue(ProcessMovieEnqueueResult result) : IProcessMovieQueue
    {
        public Guid? RequestedMovieId { get; private set; }

        public ProcessMovieEnqueueResult TryEnqueue(Guid movieId)
        {
            RequestedMovieId = movieId;
            return result;
        }
    }

    private sealed class UnusedStrmManagerClient : IStrmManagerClient
    {
        public Task<MovieLookupResult> GetByImdbIdAsync(string imdbId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("ProcessMovie must not call GetByImdbIdAsync.");

        public Task<AddMovieResult> AddMovieAsync(string imdbId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("ProcessMovie must not call AddMovieAsync.");

        public Task<ProcessMovieResult> ProcessMovieAsync(Guid movieId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The controller action must not call ProcessMovieAsync directly.");

        public Task<CatalogMoviesResult> GetPopularMoviesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException("ProcessMovie must not call GetPopularMoviesAsync.");

        public Task<MovieRowsResult> GetMovieRowsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException("ProcessMovie must not call GetMovieRowsAsync.");

        public Task<SearchMoviesResult> SearchMoviesAsync(string query, CancellationToken cancellationToken) =>
            throw new NotSupportedException("ProcessMovie must not call SearchMoviesAsync.");

        public Task<MovieDetailResult> GetMovieDetailAsync(string imdbId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("ProcessMovie must not call GetMovieDetailAsync.");
    }

    private sealed class UnusedEnsureMovieService : IEnsureMovieService
    {
        public Task<EnsureMovieResult> EnsureMovieAsync(string imdbId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("ProcessMovie must not call EnsureMovieAsync.");
    }
}
