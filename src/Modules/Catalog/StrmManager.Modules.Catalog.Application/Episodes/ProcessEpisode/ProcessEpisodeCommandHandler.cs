using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Abstractions.Data;
using StrmManager.Modules.Catalog.Application.Processing;
using StrmManager.Modules.Catalog.Domain.Episodes;
using StrmManager.Modules.Catalog.Domain.Seasons;
using StrmManager.Modules.Catalog.Domain.Shared;
using StrmManager.Modules.Catalog.Domain.SourceAttempts;
using StrmManager.Modules.Catalog.Domain.StrmFiles;
using StrmManager.Modules.MediaProcessing.Application.StrmGeneration;
using StrmManager.Modules.MediaProcessing.Application.Streams;
using StrmManager.Modules.MediaProcessing.Application.Streams.EpisodeIdentity;
using StrmManager.Modules.MediaProcessing.Application.Validation;
using ISeriesRepository = StrmManager.Modules.Catalog.Domain.Series.ISeriesRepository;
using SeriesEntity = StrmManager.Modules.Catalog.Domain.Series.Series;

namespace StrmManager.Modules.Catalog.Application.Episodes.ProcessEpisode;

/// <summary>
/// Explicit, single-episode processing pipeline (Phase 3 - no scheduler, no automatic
/// retry). Searching/Validating are transitioned in memory only and never persisted on
/// their own - a single SaveChangesAsync happens once, at whatever terminal outcome
/// (Completed/Unavailable/Error) is reached. A crash mid-pipeline therefore leaves the
/// episode exactly as it was loaded (Pending) - see ADR-011 for the full reasoning and
/// why this needs no separate Phase 4 recovery worker.
/// </summary>
internal sealed partial class ProcessEpisodeCommandHandler(
    IEpisodeRepository episodeRepository,
    ISeasonRepository seasonRepository,
    ISeriesRepository seriesRepository,
    ISourceAttemptRepository sourceAttemptRepository,
    IStrmFileRepository strmFileRepository,
    IStreamProvider streamProvider,
    IMediaValidator mediaValidator,
    IStrmWriter strmWriter,
    IUnitOfWork unitOfWork,
    IOptions<ProcessingOptions> processingOptions,
    TimeProvider timeProvider,
    ILogger<ProcessEpisodeCommandHandler> logger)
    : ICommandHandler<ProcessEpisodeCommand, ProcessEpisodeResult>
{
    public async Task<Result<ProcessEpisodeResult>> Handle(ProcessEpisodeCommand command, CancellationToken cancellationToken)
    {
        Episode? episode = await episodeRepository.GetAsync(command.EpisodeId, cancellationToken);

        if (episode is null)
        {
            return Result.Failure<ProcessEpisodeResult>(EpisodeErrors.NotFound(command.EpisodeId));
        }

        if (episode.Status == MediaStatus.Completed)
        {
            StrmFile? alreadyWrittenStrmFile = await strmFileRepository.GetForEpisodeAsync(episode.Id, cancellationToken);

            return new ProcessEpisodeResult(
                episode.Id, MediaStatus.Completed.ToString(), episode.AttemptCount, null,
                alreadyWrittenStrmFile?.Path, "Episode is already Completed - not reprocessed.");
        }

        Result startSearchingResult = episode.StartSearching(timeProvider.GetUtcNow().UtcDateTime);

        if (startSearchingResult.IsFailure)
        {
            return Result.Failure<ProcessEpisodeResult>(startSearchingResult.Error);
        }

        (Season Season, SeriesEntity Series)? catalogContext = await LoadCatalogContextAsync(episode, cancellationToken);

        if (catalogContext is null)
        {
            return await FinishAsError(episode, "Season or Series record is missing for this episode.", cancellationToken);
        }

        var streamReference = new EpisodeStreamReference(episode.ExternalId, episode.SeasonNumber, episode.EpisodeNumber, episode.Title, episode.Runtime);
        Result<IReadOnlyList<StreamCandidate>> streamsResult = await streamProvider.GetEpisodeStreamsAsync(streamReference, cancellationToken);

        if (streamsResult.IsFailure)
        {
            LogProviderFailure(logger, episode.Id, streamsResult.Error.Code);
            return await FinishAsError(episode, streamsResult.Error.Description, cancellationToken);
        }

        IReadOnlyList<StreamCandidate> candidates = streamsResult.Value;

        if (candidates.Count == 0)
        {
            return await FinishAsUnavailable(episode, "The stream provider returned no candidates.", cancellationToken);
        }

        episode.StartValidating(timeProvider.GetUtcNow().UtcDateTime);

        int attemptsThisRun = 0;
        StreamCandidate? approvedCandidate = null;

        foreach (StreamCandidate candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string candidateText = $"{candidate.Name} {candidate.Description}";
            EpisodeIdentityMatch identityMatch = EpisodeIdentityValidator.Evaluate(candidateText, episode.SeasonNumber, episode.EpisodeNumber);

            if (identityMatch != EpisodeIdentityMatch.Compatible)
            {
                string reason = identityMatch == EpisodeIdentityMatch.Conflicting
                    ? "Candidate references a different episode."
                    : "Could not confirm the candidate's episode identity.";

                RecordAttempt(episode.Id, candidate, SourceAttemptResult.Rejected, null, episode.Runtime, null, null, null, reason);
                attemptsThisRun++;
                continue;
            }

            var validationReference = new MediaValidationReference(episode.Runtime);
            MediaValidationResult validationResult = await mediaValidator.ValidateAsync(candidate, validationReference, cancellationToken);

            RecordAttempt(
                episode.Id, candidate, validationResult.Result, validationResult.Duration, validationResult.ExpectedDuration,
                validationResult.DifferencePercentage, validationResult.VideoCodec, validationResult.AudioCodec, validationResult.FailureReason);
            attemptsThisRun++;

            LogCandidateAttempt(logger, episode.Id, candidate.Name, attemptsThisRun, validationResult.Result);

            if (validationResult.Approved)
            {
                approvedCandidate = candidate;
                break;
            }
        }

        if (approvedCandidate is null)
        {
            return await FinishAsUnavailable(episode, "No candidate passed identity/media validation.", cancellationToken, attemptsThisRun);
        }

        var strmReference = new EpisodeStrmReference(
            catalogContext.Value.Series.Title, catalogContext.Value.Series.Year, episode.SeasonNumber, episode.EpisodeNumber);

        Result<string> writeResult = await strmWriter.WriteEpisodeAsync(strmReference, approvedCandidate.Url, cancellationToken);

        if (writeResult.IsFailure)
        {
            return await FinishAsError(episode, $"Failed to write .strm file: {writeResult.Error.Description}", cancellationToken, attemptsThisRun);
        }

        StrmFile? existingStrmFile = await strmFileRepository.GetForEpisodeAsync(episode.Id, cancellationToken);
        DateTime utcNow = timeProvider.GetUtcNow().UtcDateTime;

        if (existingStrmFile is null)
        {
            strmFileRepository.Insert(StrmFile.ForEpisode(episode.Id, writeResult.Value, utcNow));
        }
        else
        {
            existingStrmFile.Overwrite(writeResult.Value, utcNow);
        }

        episode.MarkCompleted(utcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        LogEpisodeCompleted(logger, episode.Id, attemptsThisRun);

        return new ProcessEpisodeResult(
            episode.Id, MediaStatus.Completed.ToString(), attemptsThisRun,
            new SelectedSourceSummary(approvedCandidate.Provider, approvedCandidate.Name), writeResult.Value, null);
    }

    private async Task<(Season Season, SeriesEntity Series)?> LoadCatalogContextAsync(Episode episode, CancellationToken cancellationToken)
    {
        Season? season = await seasonRepository.GetAsync(episode.SeasonId, cancellationToken);

        if (season is null)
        {
            return null;
        }

        SeriesEntity? series = await seriesRepository.GetAsync(season.SeriesId, cancellationToken);

        return series is null ? null : (season, series);
    }

    private void RecordAttempt(
        Guid episodeId,
        StreamCandidate candidate,
        SourceAttemptResult result,
        TimeSpan? duration,
        TimeSpan? expectedDuration,
        double? differencePercentage,
        string? videoCodec,
        string? audioCodec,
        string? failureReason)
    {
        SourceAttempt attempt = SourceAttempt.ForEpisode(
            episodeId, candidate.Provider, candidate.Name, timeProvider.GetUtcNow().UtcDateTime, result,
            duration, expectedDuration, differencePercentage, videoCodec, audioCodec, failureReason);

        sourceAttemptRepository.Insert(attempt);
    }

    private async Task<Result<ProcessEpisodeResult>> FinishAsUnavailable(
        Episode episode, string reason, CancellationToken cancellationToken, int attempts = 0)
    {
        DateTime utcNow = timeProvider.GetUtcNow().UtcDateTime;
        DateTime nextAttemptAtUtc = utcNow.Add(processingOptions.Value.UnavailableRetryDelay);

        episode.MarkUnavailable(utcNow, nextAttemptAtUtc, reason);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        LogEpisodeUnavailable(logger, episode.Id, reason);

        return new ProcessEpisodeResult(episode.Id, MediaStatus.Unavailable.ToString(), attempts, null, null, reason);
    }

    private async Task<Result<ProcessEpisodeResult>> FinishAsError(
        Episode episode, string reason, CancellationToken cancellationToken, int attempts = 0)
    {
        DateTime utcNow = timeProvider.GetUtcNow().UtcDateTime;

        episode.MarkError(utcNow, reason);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        LogEpisodeError(logger, episode.Id, reason);

        return new ProcessEpisodeResult(episode.Id, MediaStatus.Error.ToString(), attempts, null, null, reason);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stream provider failed for episode {EpisodeId}: {ErrorCode}")]
    private static partial void LogProviderFailure(ILogger logger, Guid episodeId, string errorCode);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Episode {EpisodeId}: candidate '{CandidateName}' attempt #{AttemptNumber} -> {Result}")]
    private static partial void LogCandidateAttempt(ILogger logger, Guid episodeId, string candidateName, int attemptNumber, SourceAttemptResult result);

    [LoggerMessage(Level = LogLevel.Information, Message = "Episode {EpisodeId} completed after {Attempts} attempt(s)")]
    private static partial void LogEpisodeCompleted(ILogger logger, Guid episodeId, int attempts);

    [LoggerMessage(Level = LogLevel.Information, Message = "Episode {EpisodeId} marked Unavailable: {Reason}")]
    private static partial void LogEpisodeUnavailable(ILogger logger, Guid episodeId, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Episode {EpisodeId} marked Error: {Reason}")]
    private static partial void LogEpisodeError(ILogger logger, Guid episodeId, string reason);
}
