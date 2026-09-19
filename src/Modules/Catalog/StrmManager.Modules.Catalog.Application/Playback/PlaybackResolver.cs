using Microsoft.Extensions.Logging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Domain.Movies;
using StrmManager.Modules.Catalog.Domain.Shared;
using StrmManager.Modules.Catalog.Domain.SourceAttempts;
using StrmManager.Modules.MediaProcessing.Application.Selection;
using StrmManager.Modules.MediaProcessing.Application.Streams;
using StrmManager.Modules.MediaProcessing.Application.Streams.MovieIdentity;
using StrmManager.Modules.MediaProcessing.Application.Validation;

namespace StrmManager.Modules.Catalog.Application.Playback;

/// <summary>
/// Just-in-time playback resolution for a movie (ADR-015): load the movie, ask the provider for candidates NOW,
/// run them through the same MovieSourceSelector ProcessMovie uses (custom-header rejection, positive-evidence
/// identity, media validation) and return the first approved candidate.
///
/// Read-only by construction. It is given no IUnitOfWork, ISourceAttemptRepository, IStrmWriter or clock, so it
/// cannot save, record a SourceAttempt, write a .strm or stamp a state change, and it never calls a Movie
/// transition: a playback failure leaves the movie exactly as it was (Phase 6.3 recovery is not this class's job).
/// The movie is loaded only to read its identity and metadata.
///
/// URL confidentiality: a candidate's URL goes to the media validator (via the selector) and, on success, into a
/// <see cref="PlaybackLocation"/>. It is never logged, put in a failure result or an exception, and logging never
/// includes the candidate object, its display name or any provider/validator text - only ids, counts and enums.
///
/// Cancellation simply propagates from the supplied token; there is no shared or detached work here.
/// </summary>
public sealed partial class PlaybackResolver(
    IMovieRepository movieRepository,
    IStreamProvider streamProvider,
    IMediaValidator mediaValidator,
    ILogger<PlaybackResolver> logger)
    : IPlaybackResolver
{
    public async Task<PlaybackResolutionResult> ResolveAsync(Guid movieId, CancellationToken cancellationToken)
    {
        Movie? movie = await movieRepository.GetAsync(movieId, cancellationToken);

        // One answer for "no such movie" and "not Completed": the caller must not learn which.
        if (movie is null || movie.Status != MediaStatus.Completed)
        {
            LogNotFound(logger, movieId);
            return new PlaybackResolutionResult.NotFound();
        }

        if (string.IsNullOrWhiteSpace(movie.ExternalIds.ImdbId))
        {
            return Unavailable(movieId, PlaybackUnavailableReason.MissingImdbId);
        }

        // The same references ProcessMovie builds from the Movie, as immutable values.
        var streamReference = new MovieStreamReference(movie.ExternalIds.ImdbId, movie.Title, movie.Year, movie.Runtime);
        var identityReference = new MovieIdentityReference(movie.ExternalIds.ImdbId, movie.ExternalIds.TmdbId, movie.Year);
        var validationReference = new MediaValidationReference(movie.Runtime);

        Result<IReadOnlyList<StreamCandidate>> streamsResult = await streamProvider.GetMovieStreamsAsync(streamReference, cancellationToken);

        if (streamsResult.IsFailure)
        {
            // Only the provider's stable error code is recorded - its description can carry provider text.
            LogProviderFailure(logger, movieId, streamsResult.Error.Code);
            return Unavailable(movieId, PlaybackUnavailableReason.ProviderFailure);
        }

        IReadOnlyList<StreamCandidate> candidates = streamsResult.Value;

        if (candidates.Count == 0)
        {
            return Unavailable(movieId, PlaybackUnavailableReason.NoCandidates);
        }

        int evaluated = 0;

        await foreach (MovieCandidateEvaluation evaluation in MovieSourceSelector.EvaluateAsync(
            candidates, identityReference, validationReference, mediaValidator, cancellationToken))
        {
            evaluated++;
            LogCandidateEvaluated(logger, movieId, evaluated, evaluation.Stage, evaluation.Result.Result);

            if (evaluation.Approved)
            {
                LogResolved(logger, movieId, evaluated);
                return new PlaybackResolutionResult.Resolved(
                    evaluation.Candidate.Provider, evaluation.Candidate.Name, new PlaybackLocation(evaluation.Candidate.Url));
            }
        }

        return Unavailable(movieId, PlaybackUnavailableReason.NoApprovedCandidate);
    }

    private PlaybackResolutionResult.Unavailable Unavailable(Guid movieId, PlaybackUnavailableReason reason)
    {
        LogUnavailable(logger, movieId, reason);
        return new PlaybackResolutionResult.Unavailable(reason);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Playback resolution for movie {MovieId}: not found")]
    private static partial void LogNotFound(ILogger logger, Guid movieId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stream provider failed during playback resolution for movie {MovieId}: {ErrorCode}")]
    private static partial void LogProviderFailure(ILogger logger, Guid movieId, string errorCode);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Playback resolution for movie {MovieId}: candidate #{CandidateNumber} ended at {Stage} -> {Result}")]
    private static partial void LogCandidateEvaluated(ILogger logger, Guid movieId, int candidateNumber, MovieCandidateStage stage, SourceAttemptResult result);

    [LoggerMessage(Level = LogLevel.Information, Message = "Playback resolution for movie {MovieId}: resolved after {Evaluated} candidate(s)")]
    private static partial void LogResolved(ILogger logger, Guid movieId, int evaluated);

    [LoggerMessage(Level = LogLevel.Information, Message = "Playback resolution for movie {MovieId}: unavailable ({Reason})")]
    private static partial void LogUnavailable(ILogger logger, Guid movieId, PlaybackUnavailableReason reason);
}
