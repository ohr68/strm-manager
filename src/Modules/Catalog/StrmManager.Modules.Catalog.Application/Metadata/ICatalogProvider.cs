using StrmManager.Common.Domain.Abstractions;

namespace StrmManager.Modules.Catalog.Application.Metadata;

/// <summary>
/// A source of browsable catalog rows (e.g. "Popular Movies"), keyed by nothing - unlike IMetadataProvider, this is
/// discovery, not a known-id lookup. Implemented in Infrastructure (CinemetaCatalogProvider today); Domain and the
/// rest of Application know nothing about which provider backs this. Deliberately a separate abstraction from
/// IMetadataProvider rather than an added method on it - "browse a list" and "look up one known id" are different
/// responsibilities even against the same underlying addon.
/// </summary>
public interface ICatalogProvider
{
    Task<Result<IReadOnlyList<CatalogMovie>>> GetPopularMoviesAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Free-text movie search (UI-5b) - Cinemeta's movie/top catalog with a "search" extra, verified live before
    /// implementation (UI-5a). No results is a successful empty list, never a failure.
    /// </summary>
    Task<Result<IReadOnlyList<CatalogMovie>>> SearchMoviesAsync(string query, int limit, CancellationToken cancellationToken = default);
}
