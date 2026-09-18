using StrmManager.Common.Application.Messaging;

namespace StrmManager.Modules.Catalog.Application.Seasons.GetEpisodes;

public sealed record GetSeasonEpisodesQuery(Guid SeasonId) : IQuery<IReadOnlyList<EpisodeResponse>>;
