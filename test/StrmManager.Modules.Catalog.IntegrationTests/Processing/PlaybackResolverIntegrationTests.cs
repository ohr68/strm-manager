using Microsoft.Extensions.DependencyInjection;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Playback;
using StrmManager.Modules.Catalog.Domain.Movies;
using StrmManager.Modules.Catalog.Domain.Shared;
using StrmManager.Modules.Catalog.Infrastructure.Database;
using StrmManager.Modules.Catalog.IntegrationTests.Infrastructure;
using StrmManager.Modules.MediaProcessing.Application.Streams;
using static StrmManager.Modules.Catalog.IntegrationTests.Processing.MovieCandidates;
using static StrmManager.Modules.Catalog.IntegrationTests.Processing.ProcessMovieTestSupport;

namespace StrmManager.Modules.Catalog.IntegrationTests.Processing;

/// <summary>
/// The playback resolver as the app composes it: resolved from the real container, over the real SQLite database,
/// with the real .strm writer present (wrapped so a call would show up) and the spying unit of work registered.
/// The point is to prove what the unit tests can only show by construction: resolving persists nothing, whatever
/// the outcome - no save, no state change, no SourceAttempt, no .strm - and leaks no URL into the app's logs.
/// </summary>
public class PlaybackResolverIntegrationTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ProcessMovieHost _host = new(factory);

    private static Result<IReadOnlyList<StreamCandidate>> Found(params StreamCandidate[] candidates) =>
        Result.Success<IReadOnlyList<StreamCandidate>>(candidates);

    private static object Snapshot(Movie m) =>
        (m.Id, m.ExternalIds, m.Title, m.Year, m.Runtime, m.ReleaseAtUtc, m.Status, m.LastAttemptAtUtc, m.NextAttemptAtUtc, m.AttemptCount, m.LastError, m.CreatedAtUtc, m.UpdatedAtUtc);

    private int StrmFileCount() =>
        Directory.Exists(_host.StrmRoot) ? Directory.GetFiles(_host.StrmRoot, "*", SearchOption.AllDirectories).Length : 0;

    private async Task<(PlaybackResolutionResult Result, bool DbContextHasChanges)> ResolveAsync(Guid movieId)
    {
        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        IPlaybackResolver resolver = scope.ServiceProvider.GetRequiredService<IPlaybackResolver>();

        PlaybackResolutionResult result = await resolver.ResolveAsync(movieId, CancellationToken.None);

        // Whatever the resolver did to the tracked Movie, nothing is pending in this scope's unit of work.
        return (result, scope.ServiceProvider.GetRequiredService<CatalogDbContext>().ChangeTracker.HasChanges());
    }

    private void AssertNothingPersisted() =>
        Assert.DoesNotContain(_host.Events, e => e is "plain-save" || e.StartsWith("claim-save", StringComparison.Ordinal) || e == "external:strm-writer");

    private void AssertNoUrlInLogs()
    {
        foreach (string line in _host.Logs.Lines)
        {
            Assert.DoesNotContain("media.example.test", line, StringComparison.Ordinal);
            Assert.DoesNotContain(SecretMarker, line, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task CompletedMovie_ResolvesThroughTheContainer_AndPersistsNothing()
    {
        Guid movieId = await SeedMovieAsync(_host.Services, MediaStatus.Completed);
        object before = Snapshot(await LoadMovieAsync(_host.Services, movieId));
        int strmFilesBefore = StrmFileCount();

        _host.Provider = _ => Found(Candidate("Source A"));
        _host.Validate = _ => Approval;

        (PlaybackResolutionResult result, bool hasChanges) = await ResolveAsync(movieId);

        var resolved = Assert.IsType<PlaybackResolutionResult.Resolved>(result);
        Assert.Equal(UrlFor("Source A"), resolved.Location.Reveal());

        Assert.False(hasChanges);
        AssertNothingPersisted();
        Assert.Equal(["external:stream-provider", "external:media-validator"], _host.Events.ToArray());
        Assert.Equal(before, Snapshot(await LoadMovieAsync(_host.Services, movieId))); // status, UpdatedAtUtc (the concurrency token), attempts...
        Assert.Empty(await _host.AttemptsAsync(movieId));
        Assert.Equal(strmFilesBefore, StrmFileCount());
        AssertNoUrlInLogs();
    }

    [Theory]
    [InlineData("none-approved")]
    [InlineData("no-candidates")]
    [InlineData("provider-failure")]
    public async Task UnavailableOutcomes_LeaveTheCompletedMovieExactlyAsItWas(string outcome)
    {
        Guid movieId = await SeedMovieAsync(_host.Services, MediaStatus.Completed);
        object before = Snapshot(await LoadMovieAsync(_host.Services, movieId));

        _host.Provider = outcome switch
        {
            "provider-failure" => _ => Result.Failure<IReadOnlyList<StreamCandidate>>(StreamProviderErrors.Timeout("FrostStream")),
            "no-candidates" => _ => Found(),
            _ => _ => Found(Candidate("Source A"), Candidate("Source B")),
        };
        _host.Validate = _ => Rejection();

        (PlaybackResolutionResult result, bool hasChanges) = await ResolveAsync(movieId);

        Assert.IsType<PlaybackResolutionResult.Unavailable>(result);
        Assert.False(hasChanges);
        AssertNothingPersisted();
        Assert.Equal(before, Snapshot(await LoadMovieAsync(_host.Services, movieId)));
        Assert.Equal(MediaStatus.Completed, (await LoadMovieAsync(_host.Services, movieId)).Status); // a playback failure is not a state change
        Assert.Empty(await _host.AttemptsAsync(movieId)); // not even the rejected candidates are recorded
        AssertNoUrlInLogs();
    }

    [Theory]
    [InlineData(MediaStatus.Pending)]
    [InlineData(MediaStatus.Unavailable)]
    [InlineData(MediaStatus.Error)]
    public async Task MovieThatIsNotCompleted_IsNotFound_WithoutTouchingTheProvider(MediaStatus status)
    {
        Guid movieId = await SeedMovieAsync(_host.Services, status);
        _host.Provider = _ => Found(Candidate("Source A"));
        _host.Validate = _ => Approval;

        (PlaybackResolutionResult result, _) = await ResolveAsync(movieId);

        Assert.IsType<PlaybackResolutionResult.NotFound>(result);
        Assert.Empty(_host.Events);
    }

    [Fact]
    public async Task UnknownMovie_IsNotFound_WithoutTouchingTheProvider()
    {
        (PlaybackResolutionResult result, _) = await ResolveAsync(Guid.NewGuid());

        Assert.IsType<PlaybackResolutionResult.NotFound>(result);
        Assert.Empty(_host.Events);
    }

    [Fact]
    public async Task ProviderIsAskedWithTheSameReferenceValuesProcessMovieUses()
    {
        // Two movies identical except for their (unique) IMDb id: one through ProcessMovie, one through the resolver.
        Guid pendingId = await SeedMovieAsync(_host.Services, MediaStatus.Pending, title: "Parity Movie", year: 2019);
        Guid completedId = await SeedMovieAsync(_host.Services, MediaStatus.Completed, title: "Parity Movie", year: 2019);

        var references = new List<MovieStreamReference>();
        _host.Provider = reference =>
        {
            references.Add(reference);
            return Found();
        };

        await _host.ProcessAsync(pendingId);
        await ResolveAsync(completedId);

        Assert.Equal(2, references.Count);
        MovieStreamReference fromProcessMovie = references[0];
        MovieStreamReference fromResolver = references[1];
        Assert.NotEqual(fromProcessMovie.ImdbId, fromResolver.ImdbId);
        Assert.Equal(fromProcessMovie, fromResolver with { ImdbId = fromProcessMovie.ImdbId }); // Title, Year and ExpectedRuntime match
    }
}
