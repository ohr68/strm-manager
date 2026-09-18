using System.Net;
using System.Text.Json;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Domain.Movies;
using StrmManager.Modules.Catalog.Domain.Shared;
using StrmManager.Modules.Catalog.Domain.SourceAttempts;
using StrmManager.Modules.Catalog.IntegrationTests.Infrastructure;
using StrmManager.Modules.MediaProcessing.Application.Streams;
using StrmManager.Modules.MediaProcessing.Application.Validation;
using static StrmManager.Modules.Catalog.IntegrationTests.Processing.MovieCandidates;
using static StrmManager.Modules.Catalog.IntegrationTests.Processing.ProcessMovieTestSupport;

namespace StrmManager.Modules.Catalog.IntegrationTests.Processing;

/// <summary>
/// What POST /api/movies/{id}/process does AFTER the persisted claim: provider discovery, the
/// failure / zero-result / all-rejected semantics (the +15 min and +6 h retry rules), SourceAttempt
/// recording, and the ordering/save boundaries. Candidates here carry the movie's own explicit year
/// so they get through the identity check to ffprobe (identity itself is ProcessMovieIdentityTests;
/// completion details are ProcessMovieCompletionTests). Everything external is a deterministic fake,
/// time is a FakeTimeProvider, and the .strm writer is the real one over a throwaway temp root.
/// </summary>
public class ProcessMovieDiscoveryTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ProcessMovieHost _host = new(factory);

    private static ProcessMovieResponse Parse(string body) =>
        JsonSerializer.Deserialize<ProcessMovieResponse>(body, JsonSerializerOptions.Web)!;

    private static void AssertNoUrlLeaked(string text)
    {
        Assert.DoesNotContain("media.example.test", text, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretMarker, text, StringComparison.Ordinal);
    }

    // --- Provider failure -> Error, retry in exactly 15 minutes ---

    [Theory]
    [InlineData("Streams.ProviderUnavailable")]
    [InlineData("Streams.Timeout")]
    [InlineData("Streams.InvalidResponse")]
    public async Task ProcessMovie_ProviderFails_MarksErrorAndSchedulesRetryExactlyFifteenMinutesOut(string errorCode)
    {
        Guid movieId = await SeedMovieAsync(_host.Services, MediaStatus.Pending);
        Error providerError = errorCode switch
        {
            "Streams.Timeout" => StreamProviderErrors.Timeout("FrostStream"),
            "Streams.InvalidResponse" => StreamProviderErrors.InvalidResponse("FrostStream", "malformed JSON"),
            _ => StreamProviderErrors.ProviderUnavailable("FrostStream"),
        };
        _host.Provider = _ => Result.Failure<IReadOnlyList<StreamCandidate>>(providerError);

        (HttpResponseMessage response, string body) = await _host.ProcessAsync(movieId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // an Error outcome is reported in the body, as for Episode
        ProcessMovieResponse result = Parse(body);
        Assert.Equal("Error", result.Status);
        Assert.Equal(0, result.Attempts);
        Assert.Equal(providerError.Description, result.Reason);

        Movie persisted = await LoadMovieAsync(_host.Services, movieId);
        Assert.Equal(MediaStatus.Error, persisted.Status);
        Assert.Equal(Now.AddMinutes(15), persisted.NextAttemptAtUtc); // now + RetryableErrorDelay, from the TimeProvider
        Assert.Equal(Now, persisted.LastAttemptAtUtc);
        Assert.Equal(1, persisted.AttemptCount);
        Assert.Equal(providerError.Description, persisted.LastError);

        // A provider call that produced no candidate is NOT a SourceAttempt.
        Assert.Empty(await _host.AttemptsAsync(movieId));
        Assert.Empty(_host.Validated);
        Assert.DoesNotContain("external:strm-writer", _host.Events);
    }

    // --- Zero results -> Unavailable, retry in exactly 6 hours ---

    [Fact]
    public async Task ProcessMovie_ProviderReturnsNoCandidates_MarksUnavailableAndSchedulesRetryExactlySixHoursOut()
    {
        Guid movieId = await SeedMovieAsync(_host.Services, MediaStatus.Pending);

        (HttpResponseMessage response, string body) = await _host.ProcessAsync(movieId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ProcessMovieResponse result = Parse(body);
        Assert.Equal("Unavailable", result.Status);
        Assert.Equal(0, result.Attempts);
        Assert.Equal("The stream provider returned no candidates.", result.Reason);

        Movie persisted = await LoadMovieAsync(_host.Services, movieId);
        Assert.Equal(MediaStatus.Unavailable, persisted.Status);
        Assert.Equal(Now.AddHours(6), persisted.NextAttemptAtUtc); // now + UnavailableRetryDelay, from the TimeProvider
        Assert.Equal(Now, persisted.LastAttemptAtUtc);
        Assert.Equal(1, persisted.AttemptCount);
        Assert.Equal("The stream provider returned no candidates.", persisted.LastError);

        Assert.Empty(await _host.AttemptsAsync(movieId));
        Assert.Empty(_host.Validated); // nothing to validate, so the validator was never reached
        Assert.DoesNotContain("external:strm-writer", _host.Events);
    }

    [Fact]
    public async Task ProcessMovie_MovieWithoutAnImdbId_MarksAnUnretryableErrorWithoutCallingTheProvider()
    {
        Guid movieId = await SeedMovieAsync(_host.Services, MediaStatus.Pending, withoutImdbId: true);

        (HttpResponseMessage response, string body) = await _host.ProcessAsync(movieId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Error", Parse(body).Status);

        Movie persisted = await LoadMovieAsync(_host.Services, movieId);
        Assert.Equal(MediaStatus.Error, persisted.Status);
        Assert.Null(persisted.NextAttemptAtUtc); // a permanent condition: never retried automatically
        Assert.Equal(1, persisted.AttemptCount);
        Assert.NotNull(persisted.LastError);
        Assert.DoesNotContain("external:stream-provider", _host.Events); // it could not be looked up, so it was not
    }

    // --- Candidate evaluation ---

    [Fact]
    public async Task ProcessMovie_OneCandidateRejectedByFfprobe_RecordsAMovieSourceAttemptAndMarksUnavailable()
    {
        Guid movieId = await SeedMovieAsync(_host.Services, MediaStatus.Pending);
        _host.Provider = _ => Result.Success<IReadOnlyList<StreamCandidate>>([Candidate("Source A")]);
        _host.Validate = _ => MediaValidationResult.ForRejection(
            SourceAttemptResult.Rejected, "Duration differs from the expected runtime.",
            TimeSpan.FromMinutes(40), TimeSpan.FromMinutes(100), 60.0, "h264", "aac");

        (HttpResponseMessage response, string body) = await _host.ProcessAsync(movieId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ProcessMovieResponse result = Parse(body);
        Assert.Equal("Unavailable", result.Status);
        Assert.Equal(1, result.Attempts);
        Assert.Null(result.SelectedSource);
        Assert.Null(result.StrmPath);
        AssertNoUrlLeaked(body);

        SourceAttempt attempt = Assert.Single(await _host.AttemptsAsync(movieId));
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
        Assert.DoesNotContain("external:strm-writer", _host.Events);
    }

    [Fact]
    public async Task ProcessMovie_EveryCandidateRejected_RecordsEachInProviderOrderAndMarksUnavailableForSixHours()
    {
        Guid movieId = await SeedMovieAsync(_host.Services, MediaStatus.Pending);
        _host.Provider = _ => Result.Success<IReadOnlyList<StreamCandidate>>(
            [Candidate("Source A"), Candidate("Source B"), Candidate("Source C"), Candidate("Source D")]);
        var outcomes = new Dictionary<string, (SourceAttemptResult Result, string Reason)>
        {
            ["Source A"] = (SourceAttemptResult.Rejected, "Duration differs from the expected runtime."),
            ["Source B"] = (SourceAttemptResult.Timeout, "ffprobe timed out."),
            ["Source C"] = (SourceAttemptResult.ProbeError, "ffprobe failed."),
            ["Source D"] = (SourceAttemptResult.InvalidMedia, "No video stream."),
        };
        _host.Validate = candidate => MediaValidationResult.ForRejection(outcomes[candidate.Name].Result, outcomes[candidate.Name].Reason);

        (HttpResponseMessage response, string body) = await _host.ProcessAsync(movieId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ProcessMovieResponse result = Parse(body);
        Assert.Equal("Unavailable", result.Status);
        Assert.Equal(4, result.Attempts); // candidates evaluated this run
        Assert.Equal("No candidate passed identity/media validation.", result.Reason);
        AssertNoUrlLeaked(body);

        // Every evaluated candidate is a persisted attempt, in the order the provider listed
        // them, carrying exactly the result the validator reported - no invented categories.
        IReadOnlyList<SourceAttempt> attempts = await _host.AttemptsAsync(movieId);
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
        Movie persisted = await LoadMovieAsync(_host.Services, movieId);
        DateTime finishedAtUtc = _host.Time.GetUtcNow().UtcDateTime;
        Assert.Equal(MediaStatus.Unavailable, persisted.Status);
        Assert.Equal(finishedAtUtc.AddHours(6), persisted.NextAttemptAtUtc);
        Assert.Equal(1, persisted.AttemptCount);
        Assert.Equal("No candidate passed identity/media validation.", persisted.LastError);
        Assert.DoesNotContain("external:strm-writer", _host.Events);

        // Candidates were validated in provider order, against the movie's own runtime.
        Assert.Equal(["Source A", "Source B", "Source C", "Source D"], _host.Validated.Select(v => v.CandidateName).ToArray());
        Assert.All(_host.Validated, v => Assert.Equal(new MediaValidationReference(TimeSpan.FromMinutes(100)), v.Reference));
    }

    [Fact]
    public async Task ProcessMovie_FirstCandidateRejectedSecondApproved_StopsAtTheFirstApprovalAndNeverEvaluatesTheRest()
    {
        Guid movieId = await SeedMovieAsync(_host.Services, MediaStatus.Pending);
        _host.Provider = _ => Result.Success<IReadOnlyList<StreamCandidate>>([Candidate("Source A"), Candidate("Source B"), Candidate("Source C")]);
        _host.Validate = candidate => candidate.Name == "Source B" ? Approval : Rejection();

        (HttpResponseMessage response, string body) = await _host.ProcessAsync(movieId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ProcessMovieResponse result = Parse(body);
        Assert.Equal("Completed", result.Status);
        Assert.Equal(2, result.Attempts);
        Assert.Equal(new SelectedSourceResponse("FrostStream", "Source B"), result.SelectedSource);
        Assert.Equal(["Source A", "Source B"], _host.Validated.Select(v => v.CandidateName).ToArray()); // C is never touched

        IReadOnlyList<SourceAttempt> attempts = await _host.AttemptsAsync(movieId);
        Assert.Equal(["Source A", "Source B"], attempts.Select(a => a.SourceName).ToArray());
        Assert.Equal([SourceAttemptResult.Rejected, SourceAttemptResult.Approved], attempts.Select(a => a.Result).ToArray());
        Assert.Equal(1, _host.Events.Count(e => e == "external:strm-writer")); // one .strm, for the first approved candidate only
    }

    // --- Ordering and save boundaries, as one log ---

    [Fact]
    public async Task ProcessMovie_ApprovedCandidate_SavesTheClaimFirstThenEveryLaterStepInItsOwnSave()
    {
        Guid movieId = await SeedMovieAsync(_host.Services, MediaStatus.Pending);
        _host.Provider = _ => Result.Success<IReadOnlyList<StreamCandidate>>([Candidate("Source A")]);
        _host.Validate = _ => Approval;

        await _host.ProcessAsync(movieId);

        Assert.Equal(
            [
                "claim-save:in-memory=Searching",
                "claim-save:persisted-before=Pending",
                "claim-save:result=True", // SAVE #1: the claim - durable before any external call
                "external:stream-provider",
                "plain-save", // SAVE #2: Searching -> Validating (candidates found)
                "external:media-validator",
                "external:strm-writer",
                "plain-save", // final SAVE: Completed + StrmFile + the SourceAttempt, together
            ],
            _host.Events.ToArray());
    }

    [Fact]
    public async Task ProcessMovie_AllCandidatesRejected_SavesTheClaimFirstThenEveryLaterStepInItsOwnSave()
    {
        Guid movieId = await SeedMovieAsync(_host.Services, MediaStatus.Pending);
        _host.Provider = _ => Result.Success<IReadOnlyList<StreamCandidate>>([Candidate("Source A"), Candidate("Source B")]);
        _host.Validate = _ => Rejection("no");

        await _host.ProcessAsync(movieId);

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
            _host.Events.ToArray());
    }
}
