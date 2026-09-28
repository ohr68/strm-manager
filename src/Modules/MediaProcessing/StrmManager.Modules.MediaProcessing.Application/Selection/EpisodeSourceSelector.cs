using System.Runtime.CompilerServices;
using StrmManager.Modules.Catalog.Domain.SourceAttempts;
using StrmManager.Modules.MediaProcessing.Application.Streams;
using StrmManager.Modules.MediaProcessing.Application.Streams.EpisodeIdentity;
using StrmManager.Modules.MediaProcessing.Application.Validation;

namespace StrmManager.Modules.MediaProcessing.Application.Selection;

/// <summary>
/// Episode candidate-selection policy shared by processing and playback.
///
/// Candidates are evaluated in provider order. Episode identity must be
/// compatible before media validation runs, and the first approved candidate
/// ends the selection.
/// </summary>
public static class EpisodeSourceSelector
{
    public static async IAsyncEnumerable<EpisodeCandidateEvaluation> EvaluateAsync(
        IReadOnlyList<StreamCandidate> candidates,
        int seasonNumber,
        int episodeNumber,
        MediaValidationReference validationReference,
        IMediaValidator mediaValidator,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (StreamCandidate candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string candidateText = $"{candidate.Name} {candidate.Description}";

            EpisodeIdentityMatch identityMatch =
                EpisodeIdentityValidator.Evaluate(
                    candidateText,
                    seasonNumber,
                    episodeNumber);

            if (identityMatch != EpisodeIdentityMatch.Compatible)
            {
                yield return identityMatch == EpisodeIdentityMatch.Conflicting
                    ? RejectedBeforeMediaValidation(
                        candidate,
                        EpisodeCandidateStage.IdentityConflicting,
                        "Candidate references a different episode.",
                        validationReference)
                    : RejectedBeforeMediaValidation(
                        candidate,
                        EpisodeCandidateStage.IdentityUndetermined,
                        "Could not confirm the candidate's episode identity.",
                        validationReference);

                continue;
            }

            MediaValidationResult validationResult =
                await mediaValidator.ValidateAsync(
                    candidate,
                    validationReference,
                    cancellationToken);

            yield return new EpisodeCandidateEvaluation(
                candidate,
                EpisodeCandidateStage.MediaValidation,
                validationResult);

            if (validationResult.Approved)
            {
                yield break;
            }
        }
    }

    private static EpisodeCandidateEvaluation RejectedBeforeMediaValidation(
        StreamCandidate candidate,
        EpisodeCandidateStage stage,
        string reason,
        MediaValidationReference validationReference) =>
        new(
            candidate,
            stage,
            MediaValidationResult.ForRejection(
                SourceAttemptResult.Rejected,
                reason,
                expectedDuration: validationReference.ExpectedRuntime));
}
