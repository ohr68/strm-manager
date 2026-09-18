using StrmManager.Common.Domain.Abstractions;

namespace StrmManager.Modules.Catalog.Application.Metadata;

/// <summary>
/// A source of series and movie metadata, keyed by a stable external id (e.g. an IMDb
/// id). Implemented in Infrastructure (CinemetaMetadataProvider today); Domain and the
/// rest of Application know nothing about which provider backs this. Lookup is by known
/// id only - there is deliberately no search/discovery here.
/// </summary>
public interface IMetadataProvider
{
    Task<Result<SeriesMetadata>> GetSeriesAsync(string externalId, CancellationToken cancellationToken = default);

    Task<Result<MovieMetadata>> GetMovieAsync(string externalId, CancellationToken cancellationToken = default);
}
