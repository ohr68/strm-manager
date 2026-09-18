using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Abstractions.Data;
using StrmManager.Modules.Catalog.Domain.Movies;
using StrmManager.Modules.Catalog.Domain.Shared;
using StrmManager.Modules.Catalog.Domain.SourceAttempts;
using StrmManager.Modules.Catalog.Domain.StrmFiles;
using StrmManager.Modules.Catalog.Infrastructure.Database;
using StrmManager.Modules.Catalog.IntegrationTests.Infrastructure;
using StrmManager.Modules.MediaProcessing.Application.StrmGeneration;
using StrmManager.Modules.MediaProcessing.Application.Streams;
using StrmManager.Modules.MediaProcessing.Application.Validation;
using static StrmManager.Modules.Catalog.IntegrationTests.Processing.ProcessMovieTestSupport;

namespace StrmManager.Modules.Catalog.IntegrationTests.Processing;

/// <summary>
/// Phase 6.2c: what POST /api/movies/{id}/process does AFTER the persisted claim - provider
/// discovery, the failure / zero-result / all-rejected semantics, SourceAttempt recording and
/// the stop at the pre-download boundary. Everything external is a deterministic fake and
/// time is a FakeTimeProvider (the validator fake advances it one second per candidate, so
/// attempt timestamps are distinct and ordered). Nothing here downloads or writes: a
/// recording IStrmWriter proves no write is ever attempted.
/// </summary>
public class ProcessMovieDiscoveryTests : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly MediaValidationResult Approval =
        MediaValidationResult.ForApproval(TimeSpan.FromMinutes(101), TimeSpan.FromMinutes(100), 1.0, "h264", "aac");

    private readonly HttpClient _client;
    private readonly IServiceProvider _services;
    private readonly FakeTimeProvider _time = new(Now);
    private readonly FakeStreamProvider _streamProvider = new();
    private readonly ConcurrentQueue<string> _events = new();
    private readonly ConcurrentQueue<(string CandidateName, MediaValidationReference Reference)> _validated = new();
    private readonly RecordingStrmWriter _strmWriter = new();
    private Func<StreamCandidate, MediaValidationResult> _validate =
        _ => throw new InvalidOperationException("The media validator was not expected to be called.");

    public ProcessMovieDiscoveryTests(ApiWebApplicationFactory factory)
    {
        _streamProvider.MovieHandlerAsync = (_, _) =>
        {
            _events.Enqueue("external:stream-provider");
            return Task.FromResult(Result.Success<IReadOnlyList<StreamCandidate>>([]));
        };

        var mediaValidator = new FakeMediaValidator
        {
            Handler = (candidate, reference) =>
            {
                _events.Enqueue("external:media-validator");
                _validated.Enqueue((candidate.Name, reference));
                _time.Advance(TimeSpan.FromSeconds(1)); // distinct, increasing SourceAttempt timestamps
                return _validate(candidate);
            },
        };

        WebApplicationFactory<Program> isolatedFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<TimeProvider>(_time);
                services.AddSingleton<IStreamProvider>(_streamProvider);
                services.AddSingleton<IMediaValidator>(mediaValidator);
                services.AddSingleton<IStrmWriter>(_strmWriter);
                services.AddScoped<IUnitOfWork>(sp => new SpyUnitOfWork(sp.GetRequiredService<CatalogDbContext>(), _events));
            }));

        _client = isolatedFactory.CreateClient();
        _services = isolatedFactory.Services;
    }

    private static StreamCandidate Candidate(string name) =>
        new("FrostStream", name, $"{name} description", $"https://media.example.test/{name.Replace(' ', '-')}?token=SECRET-TOKEN");

    private void ProviderReturns(params StreamCandidate[] candidates) =>
        _streamProvider.MovieHandlerAsync = (_, _) =>
        {
            _events.Enqueue("external:stream-provider");
            return Task.FromResult(Result.Success<IReadOnlyList<StreamCandidate>>(candidates));
        };

    private void ProviderFails(Error error) =>
        _streamProvider.MovieHandlerAsync = (_, _) =>
        {
            _events.Enqueue("external:stream-provider");
            return Task.FromResult(Result.Failure<IReadOnlyList<StreamCandidate>>(error));
        };

    private async Task<(HttpResponseMessage Response, string Body)> ProcessAsync(Guid movieId)
    {
        HttpResponseMessage response = await _client.PostAsync($"/api/movies/{movieId}/process", content: null);
        return (response, await response.Content.ReadAsStringAsync());
    }

    private static ProcessMovieResponse Parse(string body) =>
        JsonSerializer.Deserialize<ProcessMovieResponse>(body, JsonSerializerOptions.Web)!;

    private async Task<IReadOnlyList<SourceAttempt>> AttemptsAsync(Guid movieId)
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISourceAttemptRepository>().GetForMovieAsync(movieId);
    }

    private async Task<bool> HasStrmFileAsync(Guid movieId)
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<CatalogDbContext>()
            .Set<StrmFile>().AnyAsync(f => f.MovieId == movieId);
    }

    private static void AssertNoUrlLeaked(string text)
    {
        Assert.DoesNotContain("media.example.test", text, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET-TOKEN", text, StringComparison.Ordinal);
    }

    // --- Provider failure -> Error, retry in exactly 15 minutes ---

    [Theory]
    [InlineData("Streams.ProviderUnavailable")]
    [InlineData("Streams.Timeout")]
    [InlineData("Streams.InvalidResponse")]
    public async Task ProcessMovie_ProviderFails_MarksErrorAndSchedulesRetryExactlyFifteenMinutesOut(string errorCode)
    {
        Guid movieId = await SeedMovieAsync(_services, MediaStatus.Pending);
        Error providerError = errorCode switch
        {
            "Streams.Timeout" => StreamProviderErrors.Timeout("FrostStream"),
            "Streams.InvalidResponse" => StreamProviderErrors.InvalidResponse("FrostStream", "malformed JSON"),
            _ => StreamProviderErrors.ProviderUnavailable("FrostStream"),
        };
        ProviderFails(providerError);

        (HttpResponseMessage response, string body) = await ProcessAsync(movieId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // an Error outcome is reported in the body, as for Episode
        ProcessMovieResponse result = Parse(body);
        Assert.Equal("Error", result.Status);
        Assert.Equal(0, result.Attempts);
        Assert.Equal(providerError.Description, result.Reason);

        Movie persisted = await LoadMovieAsync(_services, movieId);
        Assert.Equal(MediaStatus.Error, persisted.Status);
        Assert.Equal(Now.AddMinutes(15), persisted.NextAttemptAtUtc); // now + RetryableErrorDelay, from the TimeProvider
        Assert.Equal(Now, persisted.LastAttemptAtUtc);
        Assert.Equal(1, persisted.AttemptCount);
        Assert.Equal(providerError.Description, persisted.LastError);

        // A provider call that produced no candidate is NOT a SourceAttempt.
        Assert.Empty(await AttemptsAsync(movieId));
        Assert.Empty(_validated);
        Assert.Equal(0, _strmWriter.Calls);
    }

    // --- Zero results -> Unavailable, retry in exactly 6 hours ---

    [Fact]
    public async Task ProcessMovie_ProviderReturnsNoCandidates_MarksUnavailableAndSchedulesRetryExactlySixHoursOut()
    {
        Guid movieId = await SeedMovieAsync(_services, MediaStatus.Pending);
        ProviderReturns();

        (HttpResponseMessage response, string body) = await ProcessAsync(movieId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ProcessMovieResponse result = Parse(body);
        Assert.Equal("Unavailable", result.Status);
        Assert.Equal(0, result.Attempts);
        Assert.Equal("The stream provider returned no candidates.", result.Reason);

        Movie persisted = await LoadMovieAsync(_services, movieId);
        Assert.Equal(MediaStatus.Unavailable, persisted.Status);
        Assert.Equal(Now.AddHours(6), persisted.NextAttemptAtUtc); // now + UnavailableRetryDelay, from the TimeProvider
        Assert.Equal(Now, persisted.LastAttemptAtUtc);
        Assert.Equal(1, persisted.AttemptCount);
        Assert.Equal("The stream provider returned no candidates.", persisted.LastError);

        Assert.Empty(await AttemptsAsync(movieId));
        Assert.Empty(_validated); // nothing to validate, so the validator was never reached
        Assert.False(await HasStrmFileAsync(movieId));
    }

    [Fact]
    public async Task ProcessMovie_MovieWithoutAnImdbId_MarksAnUnretryableErrorWithoutCallingTheProvider()
    {
        Guid movieId = await SeedMovieAsync(_services, MediaStatus.Pending, withoutImdbId: true);

        (HttpResponseMessage response, string body) = await ProcessAsync(movieId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Error", Parse(body).Status);

        Movie persisted = await LoadMovieAsync(_services, movieId);
        Assert.Equal(MediaStatus.Error, persisted.Status);
        Assert.Null(persisted.NextAttemptAtUtc); // a permanent condition: never retried automatically
        Assert.Equal(1, persisted.AttemptCount);
        Assert.NotNull(persisted.LastError);
        Assert.DoesNotContain("external:stream-provider", _events); // it could not be looked up, so it was not
    }

    // --- Candidate evaluation ---

    [Fact]
    public async Task ProcessMovie_OneCandidateRejected_RecordsAMovieSourceAttemptAndMarksUnavailable()
    {
        Guid movieId = await SeedMovieAsync(_services, MediaStatus.Pending);
        ProviderReturns(Candidate("Source A"));
        _validate = _ => MediaValidationResult.ForRejection(
            SourceAttemptResult.Rejected, "Duration differs from the expected runtime.",
            TimeSpan.FromMinutes(40), TimeSpan.FromMinutes(100), 60.0, "h264", "aac");

        (HttpResponseMessage response, string body) = await ProcessAsync(movieId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ProcessMovieResponse result = Parse(body);
        Assert.Equal("Unavailable", result.Status);
        Assert.Equal(1, result.Attempts);
        Assert.Null(result.SelectedSource);
        AssertNoUrlLeaked(body);

        SourceAttempt attempt = Assert.Single(await AttemptsAsync(movieId));
        Assert.Equal(movieId, attempt.MovieId);
        Assert.Null(attempt.EpisodeId);
        Assert.Equal("FrostStream", attempt.Provider);
        Assert.Equal("Source A", attempt.SourceName);
        Assert.Equal(SourceAttemptResult.Rejected, attempt.Result);
        Assert.Equal("Duration differs from the expected runtime.", attempt.FailureReason);
        Assert.Equal(TimeSpan.FromMinutes(40), attempt.Duration);
        Assert.Equal(TimeSpan.FromMinutes(100), attempt.ExpectedDuration);
        Assert.Equal(60.0, attempt.DifferencePercentage);
        Assert.Equal("h264", attempt.VideoCodec);
        Assert.Equal("aac", attempt.AudioCodec);
        AssertNoUrlLeaked($"{attempt.Provider} {attempt.SourceName} {attempt.FailureReason}");
    }

    [Fact]
    public async Task ProcessMovie_EveryCandidateRejected_RecordsEachInProviderOrderAndMarksUnavailableForSixHours()
    {
        Guid movieId = await SeedMovieAsync(_services, MediaStatus.Pending);
        ProviderReturns(Candidate("Source A"), Candidate("Source B"), Candidate("Source C"), Candidate("Source D"));
        var outcomes = new Dictionary<string, (SourceAttemptResult Result, string Reason)>
        {
            ["Source A"] = (SourceAttemptResult.Rejected, "Duration differs from the expected runtime."),
            ["Source B"] = (SourceAttemptResult.Timeout, "ffprobe timed out."),
            ["Source C"] = (SourceAttemptResult.ProbeError, "ffprobe failed."),
            ["Source D"] = (SourceAttemptResult.InvalidMedia, "No video stream."),
        };
        _validate = candidate => MediaValidationResult.ForRejection(outcomes[candidate.Name].Result, outcomes[candidate.Name].Reason);

        (HttpResponseMessage response, string body) = await ProcessAsync(movieId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ProcessMovieResponse result = Parse(body);
        Assert.Equal("Unavailable", result.Status);
        Assert.Equal(4, result.Attempts); // candidates evaluated this run
        Assert.Equal("No candidate passed media validation.", result.Reason);
        AssertNoUrlLeaked(body);

        // Every evaluated candidate is a persisted attempt, in the order the provider listed
        // them, carrying exactly the result the validator reported - no invented categories.
        IReadOnlyList<SourceAttempt> attempts = await AttemptsAsync(movieId);
        Assert.Equal(["Source A", "Source B", "Source C", "Source D"], attempts.Select(a => a.SourceName).ToArray());
        Assert.Equal(
            [SourceAttemptResult.Rejected, SourceAttemptResult.Timeout, SourceAttemptResult.ProbeError, SourceAttemptResult.InvalidMedia],
            attempts.Select(a => a.Result).ToArray());
        Assert.Equal(outcomes.Values.Select(o => o.Reason).ToArray(), attempts.Select(a => a.FailureReason).ToArray());
        Assert.All(attempts, a =>
        {
            Assert.Equal(movieId, a.MovieId);
            Assert.Null(a.EpisodeId);
        });

        // Same outcome as Episode when nothing passes: Unavailable, retry in UnavailableRetryDelay -
        // ONE outcome for the run (AttemptCount 1), however many candidates were tried.
        Movie persisted = await LoadMovieAsync(_services, movieId);
        DateTime finishedAtUtc = _time.GetUtcNow().UtcDateTime;
        Assert.Equal(MediaStatus.Unavailable, persisted.Status);
        Assert.Equal(finishedAtUtc.AddHours(6), persisted.NextAttemptAtUtc);
        Assert.Equal(1, persisted.AttemptCount);
        Assert.Equal("No candidate passed media validation.", persisted.LastError);
        Assert.False(await HasStrmFileAsync(movieId));
        Assert.Equal(0, _strmWriter.Calls);

        // Candidates were validated in provider order, against the movie's own runtime.
        Assert.Equal(["Source A", "Source B", "Source C", "Source D"], _validated.Select(v => v.CandidateName).ToArray());
        Assert.All(_validated, v => Assert.Equal(new MediaValidationReference(TimeSpan.FromMinutes(100)), v.Reference));
    }

    [Fact]
    public async Task ProcessMovie_FirstCandidateRejectedSecondApproved_StopsAtTheFirstApprovalAndNeverEvaluatesTheRest()
    {
        Guid movieId = await SeedMovieAsync(_services, MediaStatus.Pending);
        ProviderReturns(Candidate("Source A"), Candidate("Source B"), Candidate("Source C"));
        _validate = candidate => candidate.Name == "Source B"
            ? Approval
            : MediaValidationResult.ForRejection(SourceAttemptResult.Rejected, "Duration differs from the expected runtime.");

        (HttpResponseMessage response, string body) = await ProcessAsync(movieId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, Parse(body).Attempts);
        Assert.Equal(["Source A", "Source B"], _validated.Select(v => v.CandidateName).ToArray()); // C is never touched

        IReadOnlyList<SourceAttempt> attempts = await AttemptsAsync(movieId);
        Assert.Equal(["Source A", "Source B"], attempts.Select(a => a.SourceName).ToArray());
        Assert.Equal([SourceAttemptResult.Rejected, SourceAttemptResult.Approved], attempts.Select(a => a.Result).ToArray());
    }

    // --- The accepted candidate: stop at the pre-download boundary ---

    [Fact]
    public async Task ProcessMovie_ValidCandidate_StopsAtTheBoundaryBeforeAnyDownloadOrStrmWrite()
    {
        Guid movieId = await SeedMovieAsync(_services, MediaStatus.Pending);
        ProviderReturns(Candidate("Source A"));
        _validate = _ => Approval;

        (HttpResponseMessage response, string body) = await ProcessAsync(movieId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ProcessMovieResponse result = Parse(body);
        Assert.Equal("Validating", result.Status); // the boundary: validated, nothing written yet
        Assert.Equal(1, result.Attempts);
        Assert.Equal(new SelectedSourceResponse("FrostStream", "Source A"), result.SelectedSource);
        Assert.Null(result.StrmPath);
        AssertNoUrlLeaked(body); // the selected source is reported by provider + name, never its URL

        // The movie is persisted Validating - NOT Completed, no outcome recorded yet.
        Movie persisted = await LoadMovieAsync(_services, movieId);
        Assert.Equal(MediaStatus.Validating, persisted.Status);
        Assert.Equal(0, persisted.AttemptCount);
        Assert.Null(persisted.LastAttemptAtUtc);
        Assert.Null(persisted.NextAttemptAtUtc);

        // The approved evaluation is durable, with its media details - and no URL.
        SourceAttempt attempt = Assert.Single(await AttemptsAsync(movieId));
        Assert.Equal(SourceAttemptResult.Approved, attempt.Result);
        Assert.Equal(movieId, attempt.MovieId);
        Assert.Equal("Source A", attempt.SourceName);
        Assert.Equal(TimeSpan.FromMinutes(101), attempt.Duration);
        Assert.Equal("h264", attempt.VideoCodec);
        Assert.Null(attempt.FailureReason);
        AssertNoUrlLeaked($"{attempt.Provider} {attempt.SourceName}");

        // No download / STRM: no StrmFile row, and the writer was never even invoked.
        Assert.False(await HasStrmFileAsync(movieId));
        Assert.Equal(0, _strmWriter.Calls);
    }

    // --- Ordering and save boundaries, as one log ---

    [Fact]
    public async Task ProcessMovie_ValidCandidate_SavesTheClaimFirstThenEveryLaterStepInItsOwnSave()
    {
        Guid movieId = await SeedMovieAsync(_services, MediaStatus.Pending);
        ProviderReturns(Candidate("Source A"));
        _validate = _ => Approval;

        await ProcessAsync(movieId);

        Assert.Equal(
            [
                "claim-save:in-memory=Searching",
                "claim-save:persisted-before=Pending",
                "claim-save:result=True", // SAVE #1: the claim - durable before any external call
                "external:stream-provider",
                "plain-save", // SAVE #2: Searching -> Validating (candidates found)
                "external:media-validator",
                "plain-save", // SAVE #3: the SourceAttempt(s) - the run stops at the boundary
            ],
            _events.ToArray());
    }

    [Fact]
    public async Task ProcessMovie_AllCandidatesRejected_SavesTheClaimFirstThenEveryLaterStepInItsOwnSave()
    {
        Guid movieId = await SeedMovieAsync(_services, MediaStatus.Pending);
        ProviderReturns(Candidate("Source A"), Candidate("Source B"));
        _validate = _ => MediaValidationResult.ForRejection(SourceAttemptResult.Rejected, "no");

        await ProcessAsync(movieId);

        Assert.Equal(
            [
                "claim-save:in-memory=Searching",
                "claim-save:persisted-before=Pending",
                "claim-save:result=True",
                "external:stream-provider",
                "plain-save", // Searching -> Validating
                "external:media-validator",
                "external:media-validator",
                "plain-save", // Unavailable + both SourceAttempts, together, after all the external work
            ],
            _events.ToArray());
    }

    private sealed class RecordingStrmWriter : IStrmWriter
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<Result<string>> WriteEpisodeAsync(EpisodeStrmReference reference, string sourceUrl, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            throw new InvalidOperationException("No .strm may be written in Phase 6.2c.");
        }
    }
}
