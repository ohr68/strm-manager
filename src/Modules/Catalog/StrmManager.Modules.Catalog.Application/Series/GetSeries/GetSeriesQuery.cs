using StrmManager.Common.Application.Messaging;

namespace StrmManager.Modules.Catalog.Application.Series.GetSeries;

public sealed record GetSeriesQuery(Guid SeriesId) : IQuery<SeriesResponse>;
