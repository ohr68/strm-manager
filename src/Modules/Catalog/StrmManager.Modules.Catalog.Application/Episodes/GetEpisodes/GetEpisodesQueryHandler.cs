using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Domain.Episodes;
using StrmManager.Modules.Catalog.Domain.Seasons;

namespace StrmManager.Modules.Catalog.Application.Episodes.GetEpisodes;

internal sealed class GetEpisodesQueryHandler(IEpisodeRepository episodeRepository, ISeasonRepository seasonRepository)
    : IQueryHandler<GetEpisodesQuery, IReadOnlyList<EpisodeStatusSummary>>
{
    private const int MaxPageSize = 100;

    public async Task<Result<IReadOnlyList<EpisodeStatusSummary>>> Handle(GetEpisodesQuery query, CancellationToken cancellationToken)
    {
        int page = Math.Max(query.Page, 1);
        int pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        IReadOnlyList<Episode> episodes = await episodeRepository.GetByStatusAsync(
            query.Status, skip: (page - 1) * pageSize, take: pageSize, cancellationToken);

        var summaries = new List<EpisodeStatusSummary>(episodes.Count);

        // Season lookups are cached per query - most pages will only span a handful of
        // distinct seasons, not one lookup per episode.
        var seasonCache = new Dictionary<Guid, Season?>();

        foreach (Episode episode in episodes)
        {
            if (!seasonCache.TryGetValue(episode.SeasonId, out Season? season))
            {
                season = await seasonRepository.GetAsync(episode.SeasonId, cancellationToken);
                seasonCache[episode.SeasonId] = season;
            }

            summaries.Add(new EpisodeStatusSummary(
                episode.Id,
                episode.SeasonId,
                season?.Number ?? 0,
                episode.EpisodeNumber,
                episode.Title,
                episode.Status.ToString(),
                episode.ReleaseAtUtc,
                episode.AttemptCount,
                episode.NextAttemptAtUtc,
                episode.LastError));
        }

        return summaries;
    }
}
