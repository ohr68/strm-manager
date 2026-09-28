using Microsoft.Extensions.Logging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Domain.Episodes;
using StrmManager.Modules.Catalog.Domain.Shared;
using StrmManager.Modules.Catalog.Domain.SourceAttempts;
using StrmManager.Modules.MediaProcessing.Application.Selection;
using StrmManager.Modules.MediaProcessing.Application.Streams;
using StrmManager.Modules.MediaProcessing.Application.Validation;

namespace StrmManager.Modules.Catalog.Application.Playback;

/// <summary>
/// Just-in-time playback resolution for an episode.
///
/// The episode is loaded only to read its identity and metadata. Fresh provider
/// candidates are evaluated through the same EpisodeSourceSelector used by
/// ProcessEpisode.
///
/// Read-only by construction: no unit of work, source-attempt repository,
/// STRM writer, or clock is available to this class.
///
/// A provider URL may exist only inside PlaybackLocation after a candidate has
/// passed episode-identity and media validation.
/// </summary>
public sealed partial class EpisodePlaybackResolver(
    IEpisodeRepository episodeRepository,
    IStreamProvider streamProvider,
    IMediaValidator mediaValidator,
    ILogger<EpisodePlaybackResolver> logger)
    : IEpisodePlaybackResolver
{
    public async Task<PlaybackResolutionResult> ResolveAsync(
        Guid episodeId,
        CancellationToken cancellationToken)
    {
        Episode? episode =
            await episodeRepository.GetAsync(episodeId, cancellationToken);

        if (episode is null || episode.Status != MediaStatus.Completed)
        {
            LogNotFound(logger, episodeId);
            return new PlaybackResolutionResult.NotFound();
        }

        var streamReference = new EpisodeStreamReference(
            episode.ExternalId,
            episode.SeasonNumber,
            episode.EpisodeNumber,
            episode.Title,
            episode.Runtime);

        var validationReference =
            new MediaValidationReference(episode.Runtime);

        Result<IReadOnlyList<StreamCandidate>> streamsResult =
            await streamProvider.GetEpisodeStreamsAsync(
                streamReference,
                cancellationToken);

        if (streamsResult.IsFailure)
        {
            LogProviderFailure(
                logger,
                episodeId,
                streamsResult.Error.Code);

            return Unavailable(
                episodeId,
                PlaybackUnavailableReason.ProviderFailure);
        }

        IReadOnlyList<StreamCandidate> candidates = streamsResult.Value;

        if (candidates.Count == 0)
        {
            return Unavailable(
                episodeId,
                PlaybackUnavailableReason.NoCandidates);
        }

        int evaluated = 0;

        await foreach (EpisodeCandidateEvaluation evaluation in
            EpisodeSourceSelector.EvaluateAsync(
                candidates,
                episode.SeasonNumber,
                episode.EpisodeNumber,
                validationReference,
                mediaValidator,
                cancellationToken))
        {
            evaluated++;

            LogCandidateEvaluated(
                logger,
                episodeId,
                evaluated,
                evaluation.Stage,
                evaluation.Result.Result);

            if (evaluation.Approved)
            {
                LogResolved(logger, episodeId, evaluated);

                return new PlaybackResolutionResult.Resolved(
                    evaluation.Candidate.Provider,
                    evaluation.Candidate.Name,
                    new PlaybackLocation(evaluation.Candidate.Url));
            }
        }

        return Unavailable(
            episodeId,
            PlaybackUnavailableReason.NoApprovedCandidate);
    }

    private PlaybackResolutionResult.Unavailable Unavailable(
        Guid episodeId,
        PlaybackUnavailableReason reason)
    {
        LogUnavailable(logger, episodeId, reason);
        return new PlaybackResolutionResult.Unavailable(reason);
    }

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Playback resolution for episode {EpisodeId}: not found")]
    private static partial void LogNotFound(
        ILogger logger,
        Guid episodeId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Stream provider failed during playback resolution for episode {EpisodeId}: {ErrorCode}")]
    private static partial void LogProviderFailure(
        ILogger logger,
        Guid episodeId,
        string errorCode);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Playback resolution for episode {EpisodeId}: candidate #{CandidateNumber} ended at {Stage} -> {Result}")]
    private static partial void LogCandidateEvaluated(
        ILogger logger,
        Guid episodeId,
        int candidateNumber,
        EpisodeCandidateStage stage,
        SourceAttemptResult result);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Playback resolution for episode {EpisodeId}: resolved after {Evaluated} candidate(s)")]
    private static partial void LogResolved(
        ILogger logger,
        Guid episodeId,
        int evaluated);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Playback resolution for episode {EpisodeId}: unavailable ({Reason})")]
    private static partial void LogUnavailable(
        ILogger logger,
        Guid episodeId,
        PlaybackUnavailableReason reason);
}
