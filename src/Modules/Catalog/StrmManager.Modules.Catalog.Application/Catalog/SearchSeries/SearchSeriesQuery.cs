using StrmManager.Common.Application.Messaging;

namespace StrmManager.Modules.Catalog.Application.Catalog.SearchSeries;

public sealed record SearchSeriesQuery(string Query)
    : IQuery<SearchSeriesResponse>;
