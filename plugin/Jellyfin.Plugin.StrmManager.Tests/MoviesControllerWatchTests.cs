using Jellyfin.Plugin.StrmManager.Api;
using Jellyfin.Plugin.StrmManager.Movies;
using Jellyfin.Plugin.StrmManager.Processing;
using Jellyfin.Plugin.StrmManager.StrmManagerClient;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.StrmManager.Tests;

/// <summary>
/// Focused unit tests for MoviesController.Watch - UI-3a's watch-intent coordinator. Calls the action method
/// directly (no TestServer/running host), matching this plugin's established testing style.
/// </summary>
public sealed class MoviesControllerWatchTests
{
    private const string ImdbId = "tt0137523";
    private static readonly Guid MovieId = Guid.Parse("1393e862-e5f1-4fe6-b4a0-b3bb8ee0daa6");

    private static MoviesController CreateController(
        EnsureMovieResult ensureResult,
        ProcessMovieEnqueueResult? queueResult,
        out FakeProcessMovieQueue queue,
        MovieLookupResult? lookupResult = null)
    {
        queue = new FakeProcessMovieQueue(queueResult);
        return new MoviesController(
            new FakeStrmManagerClient(lookupResult),
            new FakeEnsureMovieService(ensureResult),
            queue,
            NullLogger<MoviesController>.Instance);
    }

    [Fact]
    public async Task Watch_PendingExistingMovie_QueueAccepted_Returns202AndEnqueues()
    {
        MoviesController controller = CreateController(
            new EnsureMovieResult.Existing(MovieId, "Pending"),
            ProcessMovieEnqueueResult.Accepted,
            out FakeProcessMovieQueue queue);

        IActionResult result = await controller.Watch(ImdbId, CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result);
        Assert.Equal(StatusCodes.Status202Accepted, accepted.StatusCode);
        AssertSnapshot(accepted.Value, MovieId, ImdbId, "Pending");
        Assert.Equal(MovieId, queue.RequestedMovieId);
    }

    [Fact]
    public async Task Watch_PendingExistingMovie_QueueAlreadyQueued_Returns202NotAConflict()
    {
        MoviesController controller = CreateController(
            new EnsureMovieResult.Existing(MovieId, "Pending"),
            ProcessMovieEnqueueResult.AlreadyQueued,
            out FakeProcessMovieQueue queue);

        IActionResult result = await controller.Watch(ImdbId, CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result);
        Assert.Equal(StatusCodes.Status202Accepted, accepted.StatusCode);
        AssertSnapshot(accepted.Value, MovieId, ImdbId, "Pending");
        Assert.Equal(MovieId, queue.RequestedMovieId);
    }

    [Fact]
    public async Task Watch_PendingExistingMovie_QueueFull_Returns429()
    {
        MoviesController controller = CreateController(
            new EnsureMovieResult.Existing(MovieId, "Pending"),
            ProcessMovieEnqueueResult.Full,
            out FakeProcessMovieQueue queue);

        IActionResult result = await controller.Watch(ImdbId, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status429TooManyRequests, objectResult.StatusCode);
        AssertSnapshot(objectResult.Value, MovieId, ImdbId, "Pending");
        Assert.Equal(MovieId, queue.RequestedMovieId);
    }

    [Theory]
    [InlineData("Scheduled")]
    [InlineData("Searching")]
    [InlineData("Validating")]
    [InlineData("Completed")]
    [InlineData("Unavailable")]
    [InlineData("Error")]
    public async Task Watch_NonPendingExistingMovie_DoesNotEnqueue_Returns202(string status)
    {
        MoviesController controller = CreateController(
            new EnsureMovieResult.Existing(MovieId, status),
            queueResult: null,
            out FakeProcessMovieQueue queue);

        IActionResult result = await controller.Watch(ImdbId, CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result);
        Assert.Equal(StatusCodes.Status202Accepted, accepted.StatusCode);
        AssertSnapshot(accepted.Value, MovieId, ImdbId, status);
        Assert.Null(queue.RequestedMovieId);
    }

    [Fact]
    public async Task Watch_CreatedMovie_LooksUpRealStatus_AndAppliesTheSameEligibilityRule()
    {
        MoviesController controller = CreateController(
            new EnsureMovieResult.Created(MovieId),
            ProcessMovieEnqueueResult.Accepted,
            out FakeProcessMovieQueue queue,
            lookupResult: new MovieLookupResult.Found(MovieId, ImdbId, "Fight Club", 1999, "Pending"));

        IActionResult result = await controller.Watch(ImdbId, CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result);
        AssertSnapshot(accepted.Value, MovieId, ImdbId, "Pending");
        Assert.Equal(MovieId, queue.RequestedMovieId);
    }

    [Fact]
    public async Task Watch_CreatedMovie_FollowUpLookupUnreachable_Returns503_AndNeverEnqueues()
    {
        MoviesController controller = CreateController(
            new EnsureMovieResult.Created(MovieId),
            queueResult: null,
            out FakeProcessMovieQueue queue,
            lookupResult: new MovieLookupResult.Unreachable("Connection refused"));

        IActionResult result = await controller.Watch(ImdbId, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, objectResult.StatusCode);
        Assert.Null(queue.RequestedMovieId);
    }

    public static IEnumerable<object[]> EnsureFailureCases()
    {
        yield return [new EnsureMovieResult.Invalid(), StatusCodes.Status400BadRequest];
        yield return [new EnsureMovieResult.NotFound(), StatusCodes.Status404NotFound];
        yield return [new EnsureMovieResult.Unreachable("Connection refused"), StatusCodes.Status503ServiceUnavailable];
        yield return [new EnsureMovieResult.Error("Unexpected outcome."), StatusCodes.Status502BadGateway];
    }

    [Theory]
    [MemberData(nameof(EnsureFailureCases))]
    public async Task Watch_EnsureFailure_MapsToExpectedStatus_AndNeverEnqueues(EnsureMovieResult ensureResult, int expectedStatusCode)
    {
        MoviesController controller = CreateController(ensureResult, queueResult: null, out FakeProcessMovieQueue queue);

        IActionResult result = await controller.Watch(ImdbId, CancellationToken.None);

        int actualStatusCode = result switch
        {
            BadRequestResult badRequest => badRequest.StatusCode,
            NotFoundResult notFound => notFound.StatusCode,
            ObjectResult objectResult => objectResult.StatusCode!.Value,
            _ => throw new InvalidOperationException($"Unexpected result type {result.GetType().Name}."),
        };
        Assert.Equal(expectedStatusCode, actualStatusCode);
        Assert.Null(queue.RequestedMovieId);
    }

    private static void AssertSnapshot(object? value, Guid expectedMovieId, string expectedImdbId, string expectedStatus)
    {
        Assert.NotNull(value);
        Guid movieId = (Guid)value.GetType().GetProperty("movieId")!.GetValue(value)!;
        var imdbId = (string)value.GetType().GetProperty("imdbId")!.GetValue(value)!;
        var status = (string)value.GetType().GetProperty("status")!.GetValue(value)!;
        Assert.Equal(expectedMovieId, movieId);
        Assert.Equal(expectedImdbId, imdbId);
        Assert.Equal(expectedStatus, status);
    }

    private sealed class FakeProcessMovieQueue(ProcessMovieEnqueueResult? result) : IProcessMovieQueue
    {
        public Guid? RequestedMovieId { get; private set; }

        public ProcessMovieEnqueueResult TryEnqueue(Guid movieId)
        {
            RequestedMovieId = movieId;
            return result ?? throw new InvalidOperationException("TryEnqueue must not be called for this test case.");
        }
    }

    private sealed class FakeEnsureMovieService(EnsureMovieResult result) : IEnsureMovieService
    {
        public Task<EnsureMovieResult> EnsureMovieAsync(string imdbId, CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class FakeStrmManagerClient(MovieLookupResult? lookupResult) : IStrmManagerClient
    {
        public Task<MovieLookupResult> GetByImdbIdAsync(string imdbId, CancellationToken cancellationToken) =>
            lookupResult is null
                ? throw new NotSupportedException("Watch must not call GetByImdbIdAsync unless resolving a newly Created movie's status.")
                : Task.FromResult(lookupResult);

        public Task<AddMovieResult> AddMovieAsync(string imdbId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Watch must not call AddMovieAsync directly.");

        public Task<ProcessMovieResult> ProcessMovieAsync(Guid movieId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Watch must not call ProcessMovieAsync directly.");

        public Task<CatalogMoviesResult> GetPopularMoviesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException("Watch must not call GetPopularMoviesAsync.");

        public Task<MovieRowsResult> GetMovieRowsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException("Watch must not call GetMovieRowsAsync.");

        public Task<SearchMoviesResult> SearchMoviesAsync(string query, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Watch must not call SearchMoviesAsync.");
    }
}
