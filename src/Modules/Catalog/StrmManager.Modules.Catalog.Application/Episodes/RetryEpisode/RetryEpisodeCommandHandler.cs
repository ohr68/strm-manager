using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Abstractions.Data;
using StrmManager.Modules.Catalog.Domain.Episodes;

namespace StrmManager.Modules.Catalog.Application.Episodes.RetryEpisode;

/// <summary>
/// Manual retry: returns an Unavailable/Error episode to Pending and returns
/// immediately - it does not process the episode synchronously. The next
/// EpisodeProcessingWorker tick (or a manual POST /api/episodes/{id}/process call)
/// claims it from there, the same as any other Pending episode. Keeping this command
/// "just flip the state" rather than "flip the state and process" avoids a second,
/// slightly-different processing entry point.
/// </summary>
internal sealed class RetryEpisodeCommandHandler(
    IEpisodeRepository episodeRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : ICommandHandler<RetryEpisodeCommand, RetryEpisodeResult>
{
    public async Task<Result<RetryEpisodeResult>> Handle(RetryEpisodeCommand command, CancellationToken cancellationToken)
    {
        Episode? episode = await episodeRepository.GetAsync(command.EpisodeId, cancellationToken);

        if (episode is null)
        {
            return Result.Failure<RetryEpisodeResult>(EpisodeErrors.NotFound(command.EpisodeId));
        }

        Result retryResult = episode.Retry(timeProvider.GetUtcNow().UtcDateTime);

        if (retryResult.IsFailure)
        {
            return Result.Failure<RetryEpisodeResult>(retryResult.Error);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new RetryEpisodeResult(episode.Id, episode.Status.ToString());
    }
}
