using StrmManager.Common.Domain.Abstractions;

namespace StrmManager.Modules.Catalog.Application.Metadata;

/// <summary>
/// A source of series metadata, keyed by a stable external id (e.g. an IMDb id).
/// Implemented in Infrastructure (CinemetaMetadataProvider today); Domain and the rest
/// of Application know nothing about which provider backs this.
/// </summary>
public interface IMetadataProvider
{
    Task<Result<SeriesMetadata>> GetSeriesAsync(string externalId, CancellationToken cancellationToken = default);
}
