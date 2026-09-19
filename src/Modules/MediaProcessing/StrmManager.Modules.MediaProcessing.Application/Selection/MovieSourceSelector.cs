using System.Runtime.CompilerServices;
using StrmManager.Modules.Catalog.Domain.SourceAttempts;
using StrmManager.Modules.MediaProcessing.Application.Streams;
using StrmManager.Modules.MediaProcessing.Application.Streams.MovieIdentity;
using StrmManager.Modules.MediaProcessing.Application.Validation;

namespace StrmManager.Modules.MediaProcessing.Application.Selection;

/// <summary>
/// The movie candidate-selection policy, extracted unchanged from ProcessMovie so a second caller can apply
/// exactly the same rules. For each candidate, in the order given:
///  1. a candidate that needs custom request headers is rejected (a plain .strm cannot carry them);
///  2. identity is evaluated from the candidate's own evidence (positive evidence only, see
///     <see cref="MovieIdentityValidator"/>): Conflicting and Undetermined are rejected, only Confirmed or
///     Compatible go on - and a rejected candidate never reaches the media validator;
///  3. the media validator runs, and its verdict is the candidate's outcome;
///  4. the first approved candidate ends the selection - later candidates are never examined.
///
/// Deliberately knows nothing about the Movie aggregate, persistence, SourceAttempt, retry policy, provider
/// discovery, .strm files or HTTP: it works on immutable input and returns what happened to each candidate.
/// It does no logging, so a candidate URL cannot leak from here; the URL only ever goes to the media validator.
///
/// The result is streamed lazily, one evaluation at a time, so the caller sees each outcome before the next
/// candidate is validated (ProcessMovie records each attempt at that point), and cancellation is checked before
/// every candidate. A caller that stops enumerating stops the selection. When the sequence ends without an
/// approved evaluation, no candidate was selected.
/// </summary>
public static class MovieSourceSelector
{
    public static async IAsyncEnumerable<MovieCandidateEvaluation> EvaluateAsync(
        IReadOnlyList<StreamCandidate> candidates,
        MovieIdentityReference identity,
        MediaValidationReference validationReference,
        IMediaValidator mediaValidator,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (StreamCandidate candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 1) Capability: unsupported, rejected before anything is spent on it. Only that fact is
            // known, never the header values.
            if (candidate.RequiresCustomHeaders)
            {
                yield return RejectedBeforeMediaValidation(
                    candidate, MovieCandidateStage.UnsupportedHeaders,
                    "Candidate requires custom request headers, which are not supported.", validationReference);
                continue;
            }

            // 2) Identity, from the candidate's own explicit evidence only.
            MovieIdentityMatch identityMatch = MovieIdentityValidator.Evaluate(
                candidate.Name, candidate.Description, candidate.TmdbId, identity);

            if (identityMatch is not (MovieIdentityMatch.Confirmed or MovieIdentityMatch.Compatible))
            {
                yield return identityMatch == MovieIdentityMatch.Conflicting
                    ? RejectedBeforeMediaValidation(
                        candidate, MovieCandidateStage.IdentityConflicting, "Candidate references a different movie.", validationReference)
                    : RejectedBeforeMediaValidation(
                        candidate, MovieCandidateStage.IdentityUndetermined, "Could not confirm the candidate's movie identity.", validationReference);
                continue;
            }

            // 3) Media validation.
            MediaValidationResult validationResult = await mediaValidator.ValidateAsync(candidate, validationReference, cancellationToken);

            yield return new MovieCandidateEvaluation(candidate, MovieCandidateStage.MediaValidation, validationResult);

            // 4) The first approved candidate wins; nothing after it is validated.
            if (validationResult.Approved)
            {
                yield break;
            }
        }
    }

    private static MovieCandidateEvaluation RejectedBeforeMediaValidation(
        StreamCandidate candidate, MovieCandidateStage stage, string reason, MediaValidationReference validationReference) =>
        new(candidate, stage, MediaValidationResult.ForRejection(SourceAttemptResult.Rejected, reason, expectedDuration: validationReference.ExpectedRuntime));
}
