using Microsoft.Extensions.Logging;
using StrmManager.Modules.Catalog.Domain.Episodes;
using StrmManager.Modules.Catalog.Domain.Seasons;

namespace StrmManager.Modules.Catalog.Application.Metadata;

/// <summary>
/// Synchronizes a series' seasons/episodes against freshly retrieved provider metadata.
/// Shared by AddSeriesCommandHandler (first sync) and RefreshSeriesMetadataCommandHandler
/// (subsequent syncs) so the idempotency/matching/status-preservation rules live in one
/// place. Does not call IUnitOfWork.SaveChangesAsync - callers own the transaction
/// boundary (see ADR-007).
/// </summary>
public sealed partial class CatalogSynchronizer(
    ISeasonRepository seasonRepository,
    IEpisodeRepository episodeRepository,
    ILogger<CatalogSynchronizer> logger)
{
    public async Task<CatalogSynchronizationResult> SynchronizeAsync(
        Guid seriesId,
        IReadOnlyList<EpisodeMetadata> episodes,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        int seasonsAdded = 0;
        int episodesAdded = 0;
        int episodesUpdated = 0;
        int episodesSkipped = 0;

        IEnumerable<IGrouping<int, EpisodeMetadata>> seasonGroups = episodes
            .Where(episode => episode.SeasonNumber >= 0)
            .GroupBy(episode => episode.SeasonNumber)
            .OrderBy(group => group.Key);

        foreach (IGrouping<int, EpisodeMetadata> seasonGroup in seasonGroups)
        {
            Season? season = await seasonRepository.GetBySeriesAndNumberAsync(seriesId, seasonGroup.Key, cancellationToken);
            bool seasonIsNew = season is null;

            if (season is null)
            {
                season = Season.Create(seriesId, seasonGroup.Key, utcNow);
                seasonRepository.Insert(season);
                seasonsAdded++;
            }

            IReadOnlyList<Episode> existingEpisodes = seasonIsNew
                ? []
                : await episodeRepository.GetBySeasonAsync(season.Id, cancellationToken);

            Dictionary<string, Episode> existingByExternalId = existingEpisodes
                .GroupBy(episode => episode.ExternalId)
                .ToDictionary(group => group.Key, group => group.First());
            Dictionary<int, Episode> existingByNumber = existingEpisodes
                .GroupBy(episode => episode.EpisodeNumber)
                .ToDictionary(group => group.Key, group => group.First());

            foreach (EpisodeMetadata episodeMetadata in seasonGroup)
            {
                if (episodeMetadata.ReleaseAtUtc is not { } releaseAtUtc)
                {
                    episodesSkipped++;
                    LogEpisodeSkippedNoReleaseDate(logger, episodeMetadata.ExternalId);
                    continue;
                }

                // Prefer the stable external id; fall back to (season, episode number)
                // for the rare case a provider changed an episode's external id between
                // syncs. Never match by title - titles can change.
                Episode? existing = existingByExternalId.GetValueOrDefault(episodeMetadata.ExternalId)
                    ?? existingByNumber.GetValueOrDefault(episodeMetadata.EpisodeNumber);

                if (existing is null)
                {
                    Episode episode = Episode.Schedule(
                        season.Id,
                        episodeMetadata.ExternalId,
                        episodeMetadata.Title,
                        seasonGroup.Key,
                        episodeMetadata.EpisodeNumber,
                        episodeMetadata.Runtime,
                        releaseAtUtc,
                        utcNow);

                    episodeRepository.Insert(episode);
                    episodesAdded++;
                }
                else
                {
                    bool changed = existing.UpdateMetadata(episodeMetadata.Title, episodeMetadata.Runtime, releaseAtUtc, utcNow);

                    // A release-date correction may have just made a still-Scheduled
                    // episode due - re-evaluate through the domain, never bypass it.
                    existing.TryBecomeEligible(utcNow);

                    if (changed)
                    {
                        episodesUpdated++;
                    }
                }
            }
        }

        return new CatalogSynchronizationResult(seasonsAdded, episodesAdded, episodesUpdated, episodesSkipped);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped episode {ExternalId} - provider did not return a release date")]
    private static partial void LogEpisodeSkippedNoReleaseDate(ILogger logger, string externalId);
}
