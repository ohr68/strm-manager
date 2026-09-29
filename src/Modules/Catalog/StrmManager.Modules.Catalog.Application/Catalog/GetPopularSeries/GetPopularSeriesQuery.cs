using StrmManager.Common.Application.Messaging;

namespace StrmManager.Modules.Catalog.Application.Catalog.GetPopularSeries;

public sealed record GetPopularSeriesQuery
    : IQuery<PopularSeriesResponse>;
