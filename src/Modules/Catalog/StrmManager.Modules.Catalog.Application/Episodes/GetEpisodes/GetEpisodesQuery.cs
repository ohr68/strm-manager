using StrmManager.Common.Application.Messaging;
using StrmManager.Modules.Catalog.Domain.Shared;

namespace StrmManager.Modules.Catalog.Application.Episodes.GetEpisodes;

public sealed record GetEpisodesQuery(MediaStatus? Status, int Page, int PageSize) : IQuery<IReadOnlyList<EpisodeStatusSummary>>;
