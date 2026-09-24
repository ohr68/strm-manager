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

    /// <summary>
    /// UI-6b's read-only Movie Detail metadata (title/description/genres/rating/poster/backdrop) - a separate,
    /// display-only sibling of <see cref="GetMovieAsync"/> that never widens <see cref="MovieMetadata"/>. Same
    /// underlying Cinemeta resource as GetMovieAsync, but this call has no relationship to AddMovie/EnsureMovie -
    /// it is never invoked by them and calling it never creates or touches a Movie.
    /// </summary>
    Task<Result<MovieDetail>> GetMovieDetailAsync(string externalId, CancellationToken cancellationToken = default);
}
