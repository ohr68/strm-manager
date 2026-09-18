using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Abstractions.Data;
using StrmManager.Modules.Catalog.Application.Processing;
using StrmManager.Modules.Catalog.Domain.Movies;
using StrmManager.Modules.Catalog.Domain.Shared;
using StrmManager.Modules.Catalog.Domain.SourceAttempts;
using StrmManager.Modules.Catalog.Domain.StrmFiles;
using StrmManager.Modules.MediaProcessing.Application.StrmGeneration;
using StrmManager.Modules.MediaProcessing.Application.Streams;
using StrmManager.Modules.MediaProcessing.Application.Streams.MovieIdentity;
using StrmManager.Modules.MediaProcessing.Application.Validation;

namespace StrmManager.Modules.Catalog.Application.Movies.ProcessMovie;

/// <summary>
/// Movie counterpart of ProcessEpisodeCommandHandler: claim -> provider lookup -> per candidate
/// (unsupported-headers check -> movie identity check -> ffprobe media validation) -> first
/// approved candidate -> write the movie .strm -> StrmFile -> Completed.
///
/// The claim comes first and is durable before anything else: the Movie is moved
/// Pending -> Searching and that claim is PERSISTED through the concurrency-aware save
/// (Movie.UpdatedAtUtc is the optimistic-concurrency token - see MovieConfiguration /
/// ADR-013) before the provider is ever called. A request that loses the race for the claim
/// ends right there, having called nothing, and the claim is never retried. Every external
/// call (provider, media validator) therefore happens strictly after that save, and each
/// later state change is persisted by its own, separate save - the claim is never folded into
/// the outcome's transaction.
///
/// Outcomes mirror Episode: a provider failure is a retryable Error (RetryableErrorDelay), no
/// candidates - or none that pass the checks - is Unavailable (UnavailableRetryDelay), and a
/// .strm write failure is a non-retryable Error. A run that gets past the claim always ends in
/// Completed, Unavailable or Error - never left Validating (short of a crash or cancellation,
/// which the maintenance sweep recovers, exactly as for Episode).
///
/// Identity (MovieIdentityValidator) is positive-evidence-only: a candidate with no explicit
/// matching IMDb id, TMDB id or "(YYYY)" is rejected without ever reaching ffprobe - the same
/// candidate list can be served for several different same-title movies, so a title is never
/// trusted. Candidates that need custom request headers are unsupported (a plain .strm cannot
/// carry them) and are rejected before identity or ffprobe.
/// </summary>
internal sealed partial class ProcessMovieCommandHandler(
    IMovieRepository movieRepository,
    ISourceAttemptRepository sourceAttemptRepository,
    IStrmFileRepository strmFileRepository,
    IStreamProvider streamProvider,
    IMediaValidator mediaValidator,
    IStrmWriter strmWriter,
    IUnitOfWork unitOfWork,
    IOptions<ProcessingOptions> processingOptions,
    TimeProvider timeProvider,
    ILogger<ProcessMovieCommandHandler> logger)
    : ICommandHandler<ProcessMovieCommand, ProcessMovieResult>
{
    public async Task<Result<ProcessMovieResult>> Handle(ProcessMovieCommand command, CancellationToken cancellationToken)
    {
        Movie? movie = await movieRepository.GetAsync(command.MovieId, cancellationToken);

        if (movie is null)
        {
            return Result.Failure<ProcessMovieResult>(MovieErrors.NotFound(command.MovieId));
        }

        if (movie.Status == MediaStatus.Completed)
        {
            StrmFile? alreadyWrittenStrmFile = await strmFileRepository.GetForMovieAsync(movie.Id, cancellationToken);

            return new ProcessMovieResult(
                movie.Id, MediaStatus.Completed.ToString(), movie.AttemptCount, null,
                alreadyWrittenStrmFile?.Path, "Movie is already Completed - not reprocessed.");
        }

        // The aggregate alone decides whether a claim is allowed (only Pending is).
        Result startSearchingResult = movie.StartSearching(timeProvider.GetUtcNow().UtcDateTime);

        if (startSearchingResult.IsFailure)
        {
            // A refused claim on a movie somebody is already working on is reported as
            // exactly that, the same outcome as losing the race below; every other refusal
            // (not released yet, Unavailable/Error awaiting Retry, ...) keeps the domain's
            // own InvalidTransition error.
            return Result.Failure<ProcessMovieResult>(
                movie.Status is MediaStatus.Searching or MediaStatus.Validating
                    ? MovieErrors.AlreadyBeingProcessed(movie.Id)
                    : startSearchingResult.Error);
        }

        // SAVE #1 - persist the claim now, before any external call. A concurrency conflict
        // means another request claimed this movie between our load and this save - that
        // request owns it, so this one stops without touching the provider. Deliberately no
        // retry of the claim.
        bool claimed = await unitOfWork.TrySaveChangesAsync(cancellationToken);

        if (!claimed)
        {
            LogClaimConflict(logger, movie.Id);
            return Result.Failure<ProcessMovieResult>(MovieErrors.AlreadyBeingProcessed(movie.Id));
        }

        // ---- From here on the movie is ours; everything below is post-claim work. ----

        if (string.IsNullOrWhiteSpace(movie.ExternalIds.ImdbId))
        {
            // Movies are looked up by IMDb id; without one there is nothing to ask the
            // provider - a permanent condition, so not automatically retried.
            return await FinishAsError(movie, "The movie has no IMDb id to look streams up by.", isRetryable: false, cancellationToken);
        }

        var streamReference = new MovieStreamReference(movie.ExternalIds.ImdbId, movie.Title, movie.Year, movie.Runtime);
        Result<IReadOnlyList<StreamCandidate>> streamsResult = await streamProvider.GetMovieStreamsAsync(streamReference, cancellationToken);

        if (streamsResult.IsFailure)
        {
            LogProviderFailure(logger, movie.Id, streamsResult.Error.Code);

            // Stream-provider failures (unavailable/timeout/invalid response) are always
            // transient/technical, never a permanent condition about this movie - eligible
            // for automatic retry (same rule as Episode, ADR-013). No SourceAttempt: no
            // candidate was ever produced to attempt.
            return await FinishAsError(movie, streamsResult.Error.Description, isRetryable: true, cancellationToken);
        }

        IReadOnlyList<StreamCandidate> candidates = streamsResult.Value;

        if (candidates.Count == 0)
        {
            return await FinishAsUnavailable(movie, "The stream provider returned no candidates.", cancellationToken);
        }

        movie.StartValidating(timeProvider.GetUtcNow().UtcDateTime);

        // SAVE #2 - Searching -> Validating, so a stuck run is diagnosable/recoverable. No
        // concurrency risk: a second claimant could only exist had it won the claim save
        // instead of this run, which is mutually exclusive.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        int attemptsThisRun = 0;
        StreamCandidate? approvedCandidate = null;
        var identityReference = new MovieIdentityReference(movie.ExternalIds.ImdbId, movie.ExternalIds.TmdbId, movie.Year);

        foreach (StreamCandidate candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 1) Capability: a stream that needs custom request headers cannot be played from a
            // plain .strm and cannot be probed by the current validator - unsupported, so it is
            // rejected before anything is spent on it. Only that fact is known, never the values.
            if (candidate.RequiresCustomHeaders)
            {
                RejectWithoutMediaValidation(movie, candidate, "Candidate requires custom request headers, which are not supported.", ref attemptsThisRun);
                continue;
            }

            // 2) Identity, from the candidate's own explicit evidence only. Anything but an
            // explicit IMDb/TMDB match or matching "(YYYY)" is rejected - and never reaches ffprobe.
            MovieIdentityMatch identityMatch = MovieIdentityValidator.Evaluate(
                candidate.Name, candidate.Description, candidate.TmdbId, identityReference);

            if (identityMatch is not (MovieIdentityMatch.Confirmed or MovieIdentityMatch.Compatible))
            {
                string reason = identityMatch == MovieIdentityMatch.Conflicting
                    ? "Candidate references a different movie."
                    : "Could not confirm the candidate's movie identity.";

                RejectWithoutMediaValidation(movie, candidate, reason, ref attemptsThisRun);
                continue;
            }

            // 3) Media validation (ffprobe).
            var validationReference = new MediaValidationReference(movie.Runtime);
            MediaValidationResult validationResult = await mediaValidator.ValidateAsync(candidate, validationReference, cancellationToken);

            // Every evaluated candidate - approved or not - is recorded, with only what the
            // SourceAttempt model holds (never the URL).
            RecordAttempt(
                movie.Id, candidate, validationResult.Result, validationResult.Duration, validationResult.ExpectedDuration,
                validationResult.DifferencePercentage, validationResult.VideoCodec, validationResult.AudioCodec, validationResult.FailureReason);
            attemptsThisRun++;

            LogCandidateAttempt(logger, movie.Id, candidate.Name, attemptsThisRun, validationResult.Result);

            if (validationResult.Approved)
            {
                approvedCandidate = candidate;
                break;
            }
        }

        if (approvedCandidate is null)
        {
            return await FinishAsUnavailable(movie, "No candidate passed identity/media validation.", cancellationToken, attemptsThisRun);
        }

        // The approved candidate's URL is the .strm's content - handed straight to the writer and
        // never logged, persisted, or put in an error or the response.
        var strmReference = new MovieStrmReference(movie.Title, movie.Year, movie.ExternalIds.ImdbId);
        Result<string> writeResult = await strmWriter.WriteMovieAsync(strmReference, approvedCandidate.Url, cancellationToken);

        if (writeResult.IsFailure)
        {
            // Filesystem/path failures (traversal rejection, permission, disk) are treated as
            // potentially persistent configuration problems, not hammered automatically - the same
            // policy as Episode (ADR-013). The movie is NOT Completed and no StrmFile is created;
            // the SourceAttempts recorded this run are persisted together with the Error.
            return await FinishAsError(movie, $"Failed to write .strm file: {writeResult.Error.Description}", isRetryable: false, cancellationToken, attemptsThisRun);
        }

        StrmFile? existingStrmFile = await strmFileRepository.GetForMovieAsync(movie.Id, cancellationToken);
        DateTime utcNow = timeProvider.GetUtcNow().UtcDateTime;

        if (existingStrmFile is null)
        {
            strmFileRepository.Insert(StrmFile.ForMovie(movie.Id, writeResult.Value, utcNow));
        }
        else
        {
            existingStrmFile.Overwrite(writeResult.Value, utcNow);
        }

        movie.MarkCompleted(utcNow);

        // Final SAVE - the Completed movie, its StrmFile and this run's SourceAttempts together.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        LogMovieCompleted(logger, movie.Id, attemptsThisRun);

        return new ProcessMovieResult(
            movie.Id, MediaStatus.Completed.ToString(), attemptsThisRun,
            new SelectedMovieSource(approvedCandidate.Provider, approvedCandidate.Name), writeResult.Value, null);
    }

    /// <summary>
    /// A candidate turned away by a check that runs before ffprobe (unsupported headers, identity):
    /// still a SourceAttempt - Rejected, with a fixed safe reason - and still counted as attempted.
    /// </summary>
    private void RejectWithoutMediaValidation(Movie movie, StreamCandidate candidate, string reason, ref int attemptsThisRun)
    {
        RecordAttempt(movie.Id, candidate, SourceAttemptResult.Rejected, null, movie.Runtime, null, null, null, reason);
        attemptsThisRun++;

        LogCandidateAttempt(logger, movie.Id, candidate.Name, attemptsThisRun, SourceAttemptResult.Rejected);
    }

    private void RecordAttempt(
        Guid movieId,
        StreamCandidate candidate,
        SourceAttemptResult result,
        TimeSpan? duration,
        TimeSpan? expectedDuration,
        double? differencePercentage,
        string? videoCodec,
        string? audioCodec,
        string? failureReason)
    {
        SourceAttempt attempt = SourceAttempt.ForMovie(
            movieId, candidate.Provider, candidate.Name, timeProvider.GetUtcNow().UtcDateTime, result,
            duration, expectedDuration, differencePercentage, videoCodec, audioCodec, failureReason);

        sourceAttemptRepository.Insert(attempt);
    }

    private async Task<Result<ProcessMovieResult>> FinishAsUnavailable(
        Movie movie, string reason, CancellationToken cancellationToken, int attempts = 0)
    {
        DateTime utcNow = timeProvider.GetUtcNow().UtcDateTime;
        DateTime nextAttemptAtUtc = utcNow.Add(processingOptions.Value.UnavailableRetryDelay);

        movie.MarkUnavailable(utcNow, nextAttemptAtUtc, reason);

        // Persists the outcome together with any SourceAttempts recorded this run - after
        // all external work, and separately from the claim.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        LogMovieUnavailable(logger, movie.Id, reason);

        return new ProcessMovieResult(movie.Id, MediaStatus.Unavailable.ToString(), attempts, null, null, reason);
    }

    private async Task<Result<ProcessMovieResult>> FinishAsError(
        Movie movie, string reason, bool isRetryable, CancellationToken cancellationToken, int attempts = 0)
    {
        DateTime utcNow = timeProvider.GetUtcNow().UtcDateTime;
        DateTime? nextAttemptAtUtc = isRetryable ? utcNow.Add(processingOptions.Value.RetryableErrorDelay) : null;

        movie.MarkError(utcNow, reason, nextAttemptAtUtc);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        LogMovieError(logger, movie.Id, reason);

        return new ProcessMovieResult(movie.Id, MediaStatus.Error.ToString(), attempts, null, null, reason);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Movie {MovieId} is already being processed by another request - claim rejected")]
    private static partial void LogClaimConflict(ILogger logger, Guid movieId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stream provider failed for movie {MovieId}: {ErrorCode}")]
    private static partial void LogProviderFailure(ILogger logger, Guid movieId, string errorCode);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Movie {MovieId}: candidate '{CandidateName}' attempt #{AttemptNumber} -> {Result}")]
    private static partial void LogCandidateAttempt(ILogger logger, Guid movieId, string candidateName, int attemptNumber, SourceAttemptResult result);

    [LoggerMessage(Level = LogLevel.Information, Message = "Movie {MovieId} completed after {Attempts} attempt(s)")]
    private static partial void LogMovieCompleted(ILogger logger, Guid movieId, int attempts);

    [LoggerMessage(Level = LogLevel.Information, Message = "Movie {MovieId} marked Unavailable: {Reason}")]
    private static partial void LogMovieUnavailable(ILogger logger, Guid movieId, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Movie {MovieId} marked Error: {Reason}")]
    private static partial void LogMovieError(ILogger logger, Guid movieId, string reason);
}
