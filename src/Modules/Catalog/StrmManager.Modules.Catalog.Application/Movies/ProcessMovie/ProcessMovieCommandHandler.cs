using Microsoft.Extensions.Logging;
using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Abstractions.Data;
using StrmManager.Modules.Catalog.Domain.Movies;
using StrmManager.Modules.Catalog.Domain.Shared;
using StrmManager.Modules.Catalog.Domain.StrmFiles;

namespace StrmManager.Modules.Catalog.Application.Movies.ProcessMovie;

/// <summary>
/// Movie counterpart of ProcessEpisodeCommandHandler, built up in stages. This stage
/// (Phase 6.2b) is the claim only: the Movie is moved Pending -> Searching and that claim
/// is PERSISTED through the concurrency-aware save (Movie.UpdatedAtUtc is the optimistic-
/// concurrency token - see MovieConfiguration / ADR-013) before anything else can happen.
/// A request that loses the race for the claim ends right there. Everything that talks to
/// the outside world (stream provider, identity and media validation, STRM writing) comes
/// after the claim in later phases, and must stay after it - which is why the only save in
/// this handler is the claim save, and why it is not retried.
/// </summary>
internal sealed partial class ProcessMovieCommandHandler(
    IMovieRepository movieRepository,
    IStrmFileRepository strmFileRepository,
    IUnitOfWork unitOfWork,
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
                movie.Id, MediaStatus.Completed.ToString(), movie.AttemptCount,
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

        // Persist the claim now, before any external work could begin. A concurrency
        // conflict means another request claimed this movie between our load and this save
        // - that request owns it, so this one stops. Deliberately no retry of the claim.
        bool claimed = await unitOfWork.TrySaveChangesAsync(cancellationToken);

        if (!claimed)
        {
            LogClaimConflict(logger, movie.Id);
            return Result.Failure<ProcessMovieResult>(MovieErrors.AlreadyBeingProcessed(movie.Id));
        }

        LogMovieClaimed(logger, movie.Id);

        // Phase 6.2b ends here: the claim is durable. Provider lookup, validation and STRM
        // generation continue from this point in later phases.
        return new ProcessMovieResult(
            movie.Id, MediaStatus.Searching.ToString(), 0, null,
            "Movie claimed for processing. Source discovery is not implemented yet.");
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Movie {MovieId} is already being processed by another request - claim rejected")]
    private static partial void LogClaimConflict(ILogger logger, Guid movieId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Movie {MovieId} claimed for processing")]
    private static partial void LogMovieClaimed(ILogger logger, Guid movieId);
}
