using StrmManager.Common.Application.Messaging;

namespace StrmManager.Modules.Catalog.Application.Series.GetSeasons;

public sealed record GetSeriesSeasonsQuery(Guid SeriesId) : IQuery<IReadOnlyList<SeasonResponse>>;
