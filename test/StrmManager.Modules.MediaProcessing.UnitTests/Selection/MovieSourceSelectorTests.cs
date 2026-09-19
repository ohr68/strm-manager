using StrmManager.Modules.Catalog.Domain.SourceAttempts;
using StrmManager.Modules.MediaProcessing.Application.Selection;
using StrmManager.Modules.MediaProcessing.Application.Streams;
using StrmManager.Modules.MediaProcessing.Application.Streams.MovieIdentity;
using StrmManager.Modules.MediaProcessing.Application.Validation;

namespace StrmManager.Modules.MediaProcessing.UnitTests.Selection;

/// <summary>
/// Characterizes the candidate-selection policy that ProcessMovie applied inline before it was extracted.
/// Identity fixtures are checked against MovieIdentityValidator itself, so a fixture cannot silently mean
/// something else; the selector does not reinterpret any of those rules.
/// </summary>
public class MovieSourceSelectorTests
{
    private const string Imdb = "tt0111161";
    private const int Year = 1994;
    private const string SecretUrl = "https://canary-host-2b7d.invalid/PATHSECRET-91/master.m3u8?sig=QUERYSECRET-44";

    private static readonly MovieIdentityReference Identity = new(Imdb, "278", Year);
    private static readonly TimeSpan Runtime = TimeSpan.FromMinutes(142);
    private static readonly MediaValidationReference Validation = new(Runtime);

    private static StreamCandidate Confirmed(string label) => new("FrostStream", $"{label} {Imdb}", null, SecretUrl + "&c=" + label);

    private static StreamCandidate Compatible(string label) => new("FrostStream", $"{label} ({Year})", null, SecretUrl + "&c=" + label);

    private static StreamCandidate Conflicting(string label) => new("FrostStream", $"{label} (2019)", null, SecretUrl + "&c=" + label);

    private static StreamCandidate Undetermined(string label) => new("FrostStream", label, null, SecretUrl + "&c=" + label);

    private static MediaValidationResult Approved() =>
        MediaValidationResult.ForApproval(Runtime, Runtime, 0, "h264", "aac");

    private static MediaValidationResult Bad(SourceAttemptResult result = SourceAttemptResult.InvalidMedia) =>
        MediaValidationResult.ForRejection(result, "not playable", TimeSpan.FromMinutes(5), Runtime, 96.5, "mpeg4", null);

    private static async Task<List<MovieCandidateEvaluation>> RunAsync(
        IReadOnlyList<StreamCandidate> candidates, RecordingValidator validator, CancellationToken cancellationToken = default)
    {
        var evaluations = new List<MovieCandidateEvaluation>();

        await foreach (MovieCandidateEvaluation evaluation in MovieSourceSelector.EvaluateAsync(candidates, Identity, Validation, validator, cancellationToken))
        {
            evaluations.Add(evaluation);
        }

        return evaluations;
    }

    [Fact]
    public void Fixtures_HaveTheIdentityTheyClaim()
    {
        Assert.Equal(MovieIdentityMatch.Confirmed, Evaluate(Confirmed("a")));
        Assert.Equal(MovieIdentityMatch.Compatible, Evaluate(Compatible("a")));
        Assert.Equal(MovieIdentityMatch.Conflicting, Evaluate(Conflicting("a")));
        Assert.Equal(MovieIdentityMatch.Undetermined, Evaluate(Undetermined("a")));

        static MovieIdentityMatch Evaluate(StreamCandidate c) => MovieIdentityValidator.Evaluate(c.Name, c.Description, c.TmdbId, Identity);
    }

    [Fact]
    public async Task FirstApprovedCandidateWins_AndLaterCandidatesAreNeverValidated()
    {
        var validator = new RecordingValidator(name => name.StartsWith("bad", StringComparison.Ordinal) ? Bad() : Approved());
        StreamCandidate[] candidates = [Compatible("bad-1"), Compatible("good-2"), Compatible("good-3")];

        List<MovieCandidateEvaluation> evaluations = await RunAsync(candidates, validator);

        Assert.Equal(2, evaluations.Count);
        Assert.False(evaluations[0].Approved);
        Assert.True(evaluations[1].Approved);
        Assert.Same(candidates[1], evaluations[1].Candidate);
        Assert.Equal(2, validator.Calls.Count); // good-3 was never validated
        Assert.DoesNotContain(validator.Calls, call => call.Contains("good-3", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CandidateRequiringCustomHeaders_NeverReachesIdentityOrMediaValidation()
    {
        var validator = new RecordingValidator(_ => Approved());
        // Carries positive identity evidence, so only the header check can be what stops it.
        StreamCandidate candidate = Confirmed("headers") with { RequiresCustomHeaders = true };

        List<MovieCandidateEvaluation> evaluations = await RunAsync([candidate], validator);

        MovieCandidateEvaluation evaluation = Assert.Single(evaluations);
        Assert.Empty(validator.Calls);
        Assert.Equal(MovieCandidateStage.UnsupportedHeaders, evaluation.Stage);
        AssertRejectedBeforeMediaValidation(evaluation, "Candidate requires custom request headers, which are not supported.");
    }

    [Fact]
    public async Task ConflictingIdentity_NeverReachesTheMediaValidator()
    {
        var validator = new RecordingValidator(_ => Approved());

        List<MovieCandidateEvaluation> evaluations = await RunAsync([Conflicting("other-movie")], validator);

        MovieCandidateEvaluation evaluation = Assert.Single(evaluations);
        Assert.Empty(validator.Calls);
        Assert.Equal(MovieCandidateStage.IdentityConflicting, evaluation.Stage);
        AssertRejectedBeforeMediaValidation(evaluation, "Candidate references a different movie.");
    }

    [Fact]
    public async Task UndeterminedIdentity_NeverReachesTheMediaValidator()
    {
        var validator = new RecordingValidator(_ => Approved());

        List<MovieCandidateEvaluation> evaluations = await RunAsync([Undetermined("no-evidence")], validator);

        MovieCandidateEvaluation evaluation = Assert.Single(evaluations);
        Assert.Empty(validator.Calls);
        Assert.Equal(MovieCandidateStage.IdentityUndetermined, evaluation.Stage);
        AssertRejectedBeforeMediaValidation(evaluation, "Could not confirm the candidate's movie identity.");
    }

    [Fact]
    public async Task ConfirmedIdentity_ProceedsToMediaValidation()
    {
        var validator = new RecordingValidator(_ => Approved());

        List<MovieCandidateEvaluation> evaluations = await RunAsync([Confirmed("confirmed")], validator);

        Assert.Single(validator.Calls);
        Assert.Equal(MovieCandidateStage.MediaValidation, Assert.Single(evaluations).Stage);
        Assert.True(evaluations[0].Approved);
    }

    [Fact]
    public async Task CompatibleIdentity_ProceedsToMediaValidation()
    {
        var validator = new RecordingValidator(_ => Approved());

        List<MovieCandidateEvaluation> evaluations = await RunAsync([Compatible("compatible")], validator);

        Assert.Single(validator.Calls);
        Assert.Equal(MovieCandidateStage.MediaValidation, Assert.Single(evaluations).Stage);
        Assert.True(evaluations[0].Approved);
    }

    [Fact]
    public async Task RejectedMediaCandidate_ContinuesToTheNextCandidate()
    {
        var validator = new RecordingValidator(name => name.Contains("first", StringComparison.Ordinal) ? Bad(SourceAttemptResult.Timeout) : Approved());

        List<MovieCandidateEvaluation> evaluations = await RunAsync([Compatible("first"), Compatible("second")], validator);

        Assert.Equal(2, evaluations.Count);
        Assert.False(evaluations[0].Approved);
        Assert.Equal(SourceAttemptResult.Timeout, evaluations[0].Result.Result);
        Assert.True(evaluations[1].Approved);
        Assert.Equal(2, validator.Calls.Count);
    }

    [Fact]
    public async Task NoApprovedCandidate_ProducesNoSelection_AndEveryCandidateIsAccountedFor()
    {
        var validator = new RecordingValidator(_ => Bad());
        StreamCandidate[] candidates =
        [
            Confirmed("media-bad"),
            Conflicting("conflict"),
            Undetermined("undetermined"),
            Compatible("media-bad-2") with { RequiresCustomHeaders = true },
            Compatible("media-bad-3"),
        ];

        List<MovieCandidateEvaluation> evaluations = await RunAsync(candidates, validator);

        Assert.Equal(candidates.Length, evaluations.Count); // one evaluation per candidate = one SourceAttempt each
        Assert.DoesNotContain(evaluations, e => e.Approved);
        Assert.Equal(2, validator.Calls.Count); // only the two identity-passing, header-free candidates
    }

    [Fact]
    public async Task EmptyCandidateList_ProducesNothing()
    {
        var validator = new RecordingValidator(_ => Approved());

        Assert.Empty(await RunAsync([], validator));
        Assert.Empty(validator.Calls);
    }

    [Fact]
    public async Task CandidateOrder_IsPreserved_InEvaluationsAndValidatorCalls()
    {
        var validator = new RecordingValidator(_ => Bad());
        StreamCandidate[] candidates =
        [
            Compatible("c1"), Undetermined("c2"), Confirmed("c3"), Conflicting("c4"), Compatible("c5"),
        ];

        List<MovieCandidateEvaluation> evaluations = await RunAsync(candidates, validator);

        Assert.Equal(candidates, evaluations.Select(e => e.Candidate));
        Assert.Equal(["c1 (1994)", "c3 tt0111161", "c5 (1994)"], validator.Calls);
    }

    [Fact]
    public async Task MediaValidationEvaluation_CarriesTheValidatorsResultUnchanged()
    {
        MediaValidationResult verdict = Bad(SourceAttemptResult.InvalidMedia);
        var validator = new RecordingValidator(_ => verdict);

        List<MovieCandidateEvaluation> evaluations = await RunAsync([Compatible("media")], validator);

        // A SourceAttempt is built from exactly these fields; the selector must not reshape them.
        Assert.Same(verdict, Assert.Single(evaluations).Result);
    }

    [Fact]
    public async Task PreValidationRejection_CarriesExactlyWhatProcessMovieRecordedBefore()
    {
        var validator = new RecordingValidator(_ => Approved());

        MovieCandidateEvaluation evaluation = Assert.Single(await RunAsync([Undetermined("x")], validator));

        // Formerly: RecordAttempt(..., SourceAttemptResult.Rejected, null, movie.Runtime, null, null, null, reason)
        Assert.False(evaluation.Result.Approved);
        Assert.Equal(SourceAttemptResult.Rejected, evaluation.Result.Result);
        Assert.Null(evaluation.Result.Duration);
        Assert.Equal(Runtime, evaluation.Result.ExpectedDuration);
        Assert.Null(evaluation.Result.DifferencePercentage);
        Assert.Null(evaluation.Result.VideoCodec);
        Assert.Null(evaluation.Result.AudioCodec);
        Assert.NotNull(evaluation.Result.FailureReason);
    }

    [Fact]
    public async Task MediaValidator_ReceivesTheValidationReference_AndTheCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        var validator = new RecordingValidator(_ => Approved());

        await RunAsync([Compatible("a")], validator, cts.Token);

        Assert.Equal([Validation], validator.References);
        Assert.Equal([cts.Token], validator.Tokens);
    }

    [Fact]
    public async Task Selection_IsLazy_EachCandidateIsValidatedOnlyWhenTheCallerAsksForIt()
    {
        var validator = new RecordingValidator(_ => Bad());

        await using IAsyncEnumerator<MovieCandidateEvaluation> enumerator =
            MovieSourceSelector.EvaluateAsync([Compatible("a"), Compatible("b")], Identity, Validation, validator).GetAsyncEnumerator();

        Assert.Empty(validator.Calls); // nothing happens until enumeration starts

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Single(validator.Calls); // the caller sees candidate 1's verdict before candidate 2 is validated

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(2, validator.Calls.Count);
    }

    [Fact]
    public async Task Cancellation_BeforeTheFirstCandidate_ThrowsWithoutValidating()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var validator = new RecordingValidator(_ => Approved());

        await Assert.ThrowsAsync<OperationCanceledException>(() => RunAsync([Compatible("a")], validator, cts.Token));

        Assert.Empty(validator.Calls);
    }

    [Fact]
    public async Task Cancellation_BetweenCandidates_ThrowsBeforeTheNextCandidateIsExamined()
    {
        using var cts = new CancellationTokenSource();
        var validator = new RecordingValidator(_ =>
        {
            cts.Cancel(); // e.g. the caller went away while candidate 1 was being probed
            return Bad();
        });
        var seen = new List<MovieCandidateEvaluation>();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (MovieCandidateEvaluation e in MovieSourceSelector.EvaluateAsync(
                [Compatible("a"), Compatible("b")], Identity, Validation, validator, cts.Token))
            {
                seen.Add(e);
            }
        });

        Assert.Single(seen); // candidate 1's verdict was delivered first, as it was recorded before the next check
        Assert.Single(validator.Calls);
    }

    [Fact]
    public async Task ValidatorCancellation_PropagatesUnchanged()
    {
        var validator = new RecordingValidator(_ => throw new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() => RunAsync([Compatible("a")], validator));
    }

    [Fact]
    public async Task EvaluationText_NeverContainsTheCandidateUrl()
    {
        var validator = new RecordingValidator(_ => Bad());
        List<MovieCandidateEvaluation> evaluations = await RunAsync([Compatible("a"), Undetermined("b"), Compatible("c") with { RequiresCustomHeaders = true }], validator);

        foreach (MovieCandidateEvaluation evaluation in evaluations)
        {
            string text = evaluation.ToString();
            Assert.DoesNotContain("canary-host", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SECRET", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(evaluation.Candidate.Url, text, StringComparison.Ordinal);
        }
    }

    private static void AssertRejectedBeforeMediaValidation(MovieCandidateEvaluation evaluation, string reason)
    {
        Assert.False(evaluation.Approved);
        Assert.Equal(SourceAttemptResult.Rejected, evaluation.Result.Result);
        Assert.Equal(reason, evaluation.Result.FailureReason);
        Assert.Equal(Runtime, evaluation.Result.ExpectedDuration);
        Assert.Null(evaluation.Result.Duration);
    }

    private sealed class RecordingValidator(Func<string, MediaValidationResult> verdict) : IMediaValidator
    {
        public List<string> Calls { get; } = [];

        public List<MediaValidationReference> References { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];

        public Task<MediaValidationResult> ValidateAsync(
            StreamCandidate candidate, MediaValidationReference reference, CancellationToken cancellationToken = default)
        {
            Calls.Add(candidate.Name);
            References.Add(reference);
            Tokens.Add(cancellationToken);
            return Task.FromResult(verdict(candidate.Name));
        }
    }
}
