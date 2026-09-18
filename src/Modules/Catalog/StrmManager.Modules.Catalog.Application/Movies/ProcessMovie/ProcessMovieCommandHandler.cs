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
using StrmManager.Modules.MediaProcessing.Application.Streams;
using StrmManager.Modules.MediaProcessing.Application.Validation;

namespace StrmManager.Modules.Catalog.Application.Movies.ProcessMovie;

/// <summary>
/// Movie counterpart of ProcessEpisodeCommandHandler, built up in stages. Through Phase 6.2c
/// it runs: claim -> provider lookup -> media validation of the candidates -> stop at the
/// boundary before download / STRM generation (a later phase).
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
/// candidates - or none that pass validation - is Unavailable (UnavailableRetryDelay), and a
/// candidate that passes leaves the Movie Validating with its SourceAttempts persisted.
/// </summary>
internal sealed partial class ProcessMovieCommandHandler(
    IMovieRepository movieRepository,
    ISourceAttemptRepository sourceAttemptRepository,
    IStrmFileRepository strmFileRepository,
    IStreamProvider streamProvider,
    IMediaValidator mediaValidator,
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

        foreach (StreamCandidate candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

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
            return await FinishAsUnavailable(movie, "No candidate passed media validation.", cancellationToken, attemptsThisRun);
        }

        // SAVE #3 - the boundary. A candidate passed, and this phase stops here: the movie
        // stays Validating (there is no download or STRM step yet, so it is neither
        // Completed nor given an outcome), and the SourceAttempts - the approved one
        // included - are persisted. Continuing from this point is a later phase's job.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        LogValidCandidateFound(logger, movie.Id, attemptsThisRun);

        return new ProcessMovieResult(
            movie.Id, MediaStatus.Validating.ToString(), attemptsThisRun,
            new SelectedMovieSource(approvedCandidate.Provider, approvedCandidate.Name), null,
            "A candidate passed validation. Download and STRM generation are not implemented yet.");
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Movie {MovieId}: a candidate passed validation after {Attempts} attempt(s) - stopping before download")]
    private static partial void LogValidCandidateFound(ILogger logger, Guid movieId, int attempts);

    [LoggerMessage(Level = LogLevel.Information, Message = "Movie {MovieId} marked Unavailable: {Reason}")]
    private static partial void LogMovieUnavailable(ILogger logger, Guid movieId, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Movie {MovieId} marked Error: {Reason}")]
    private static partial void LogMovieError(ILogger logger, Guid movieId, string reason);
}
