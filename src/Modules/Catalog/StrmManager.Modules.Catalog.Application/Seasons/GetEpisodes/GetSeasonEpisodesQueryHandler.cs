using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Domain.Episodes;
using StrmManager.Modules.Catalog.Domain.Seasons;

namespace StrmManager.Modules.Catalog.Application.Seasons.GetEpisodes;

internal sealed class GetSeasonEpisodesQueryHandler(
    ISeasonRepository seasonRepository,
    IEpisodeRepository episodeRepository)
    : IQueryHandler<GetSeasonEpisodesQuery, IReadOnlyList<EpisodeResponse>>
{
    public async Task<Result<IReadOnlyList<EpisodeResponse>>> Handle(GetSeasonEpisodesQuery query, CancellationToken cancellationToken)
    {
        Season? season = await seasonRepository.GetAsync(query.SeasonId, cancellationToken);

        if (season is null)
        {
            return Result.Failure<IReadOnlyList<EpisodeResponse>>(SeasonErrors.NotFound(query.SeasonId));
        }

        IReadOnlyList<Episode> episodes = await episodeRepository.GetBySeasonAsync(query.SeasonId, cancellationToken);

        IReadOnlyList<EpisodeResponse> response = episodes
            .OrderBy(episode => episode.EpisodeNumber)
            .Select(episode => new EpisodeResponse(
                episode.Id,
                episode.EpisodeNumber,
                episode.Title,
                episode.Status.ToString(),
                episode.ReleaseAtUtc,
                episode.AttemptCount,
                episode.LastError))
            .ToList();

        return Result.Success(response);
    }
}
