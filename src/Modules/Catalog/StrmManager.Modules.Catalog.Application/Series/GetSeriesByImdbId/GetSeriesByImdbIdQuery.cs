using StrmManager.Common.Application.Messaging;
using StrmManager.Modules.Catalog.Application.Series.GetSeries;

namespace StrmManager.Modules.Catalog.Application.Series.GetSeriesByImdbId;

/// <summary>
/// Looks a series up by its IMDb id and returns the same SeriesResponse as GetSeries.
/// Purely observational: it never calls a metadata provider and never changes the series.
/// </summary>
public sealed record GetSeriesByImdbIdQuery(string ImdbId)
    : IQuery<SeriesResponse>;
