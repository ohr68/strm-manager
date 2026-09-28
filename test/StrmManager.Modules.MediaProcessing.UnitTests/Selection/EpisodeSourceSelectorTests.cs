using StrmManager.Modules.Catalog.Domain.SourceAttempts;
using StrmManager.Modules.MediaProcessing.Application.Selection;
using StrmManager.Modules.MediaProcessing.Application.Streams;
using StrmManager.Modules.MediaProcessing.Application.Validation;

namespace StrmManager.Modules.MediaProcessing.UnitTests.Selection;

public class EpisodeSourceSelectorTests
{
    private const int Season = 2;
    private const int Episode = 7;
    private const string SecretUrl =
        "https://canary-host.invalid/PATHSECRET/master.m3u8?sig=QUERYSECRET";

    private static readonly TimeSpan Runtime = TimeSpan.FromMinutes(48);
    private static readonly MediaValidationReference Validation = new(Runtime);

    private static StreamCandidate Candidate(string label, string identity) =>
        new(
            "FrostStream",
            $"{label} {identity}",
            null,
            SecretUrl + "&c=" + label);

    private static MediaValidationResult Approved() =>
        MediaValidationResult.ForApproval(
            Runtime,
            Runtime,
            0,
            "h264",
            "aac");

    private static MediaValidationResult Rejected() =>
        MediaValidationResult.ForRejection(
            SourceAttemptResult.InvalidMedia,
            "not playable",
            TimeSpan.FromMinutes(5),
            Runtime,
            89.5,
            "mpeg4",
            null);

    private static async Task<List<EpisodeCandidateEvaluation>> RunAsync(
        IReadOnlyList<StreamCandidate> candidates,
        RecordingValidator validator,
        CancellationToken cancellationToken = default)
    {
        var evaluations = new List<EpisodeCandidateEvaluation>();

        await foreach (EpisodeCandidateEvaluation evaluation in
            EpisodeSourceSelector.EvaluateAsync(
                candidates,
                Season,
                Episode,
                Validation,
                validator,
                cancellationToken))
        {
            evaluations.Add(evaluation);
        }

        return evaluations;
    }

    [Fact]
    public async Task FirstApprovedCandidateWins_AndLaterCandidatesAreNeverValidated()
    {
        var validator = new RecordingValidator(
            name => name.StartsWith("bad", StringComparison.Ordinal)
                ? Rejected()
                : Approved());

        StreamCandidate[] candidates =
        [
            Candidate("bad-1", "S02E07"),
            Candidate("good-2", "S02E07"),
            Candidate("good-3", "S02E07"),
        ];

        List<EpisodeCandidateEvaluation> evaluations =
            await RunAsync(candidates, validator);

        Assert.Equal(2, evaluations.Count);
        Assert.False(evaluations[0].Approved);
        Assert.True(evaluations[1].Approved);
        Assert.Same(candidates[1], evaluations[1].Candidate);
        Assert.Equal(2, validator.Calls.Count);
    }

    [Fact]
    public async Task ConflictingIdentity_NeverReachesMediaValidation()
    {
        var validator = new RecordingValidator(_ => Approved());

        List<EpisodeCandidateEvaluation> evaluations =
            await RunAsync(
                [Candidate("wrong", "S02E08")],
                validator);

        EpisodeCandidateEvaluation evaluation = Assert.Single(evaluations);

        Assert.Empty(validator.Calls);
        Assert.Equal(
            EpisodeCandidateStage.IdentityConflicting,
            evaluation.Stage);
        Assert.Equal(SourceAttemptResult.Rejected, evaluation.Result.Result);
        Assert.Equal(
            "Candidate references a different episode.",
            evaluation.Result.FailureReason);
    }

    [Fact]
    public async Task UndeterminedIdentity_NeverReachesMediaValidation()
    {
        var validator = new RecordingValidator(_ => Approved());

        List<EpisodeCandidateEvaluation> evaluations =
            await RunAsync(
                [Candidate("unknown", "no-episode-identity")],
                validator);

        EpisodeCandidateEvaluation evaluation = Assert.Single(evaluations);

        Assert.Empty(validator.Calls);
        Assert.Equal(
            EpisodeCandidateStage.IdentityUndetermined,
            evaluation.Stage);
        Assert.Equal(
            "Could not confirm the candidate's episode identity.",
            evaluation.Result.FailureReason);
    }

    [Fact]
    public async Task CompatibleIdentity_ProceedsToMediaValidation()
    {
        var validator = new RecordingValidator(_ => Approved());

        List<EpisodeCandidateEvaluation> evaluations =
            await RunAsync(
                [Candidate("episode", "S02E07")],
                validator);

        EpisodeCandidateEvaluation evaluation = Assert.Single(evaluations);

        Assert.Single(validator.Calls);
        Assert.Equal(
            EpisodeCandidateStage.MediaValidation,
            evaluation.Stage);
        Assert.True(evaluation.Approved);
        Assert.Same(Validation, validator.References.Single());
    }

    [Fact]
    public async Task EvaluationText_NeverContainsCandidateUrl()
    {
        var validator = new RecordingValidator(_ => Rejected());

        List<EpisodeCandidateEvaluation> evaluations =
            await RunAsync(
                [
                    Candidate("media", "S02E07"),
                    Candidate("wrong", "S02E08"),
                ],
                validator);

        foreach (EpisodeCandidateEvaluation evaluation in evaluations)
        {
            string text = evaluation.ToString();

            Assert.DoesNotContain(
                "canary-host",
                text,
                StringComparison.OrdinalIgnoreCase);

            Assert.DoesNotContain(
                "SECRET",
                text,
                StringComparison.OrdinalIgnoreCase);

            Assert.DoesNotContain(
                evaluation.Candidate.Url,
                text,
                StringComparison.Ordinal);
        }
    }

    private sealed class RecordingValidator(
        Func<string, MediaValidationResult> verdict)
        : IMediaValidator
    {
        public List<string> Calls { get; } = [];

        public List<MediaValidationReference> References { get; } = [];

        public Task<MediaValidationResult> ValidateAsync(
            StreamCandidate candidate,
            MediaValidationReference reference,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(candidate.Name);
            References.Add(reference);

            return Task.FromResult(verdict(candidate.Name));
        }
    }
}
