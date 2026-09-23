using StrmManager.Modules.Catalog.Application.Metadata;

namespace StrmManager.Modules.Catalog.Infrastructure.Metadata.Cinemeta;

/// <summary>
/// Converts one raw Cinemeta catalog item into the provider-neutral CatalogMovie. Deliberately a separate, small
/// mapper from CinemetaMetadataMapper (different DTO, different failure shape - a catalog item with no id/name is
/// simply skipped by the caller, never a Result failure) even though both convert Cinemeta data.
/// </summary>
internal static class CinemetaCatalogMapper
{
    /// <summary>Null for an item missing what a catalog card needs (id/name) - the caller skips it rather than failing the whole row.</summary>
    public static CatalogMovie? Map(CinemetaCatalogItemDto item)
    {
        if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Name))
        {
            return null;
        }

        int? year = CinemetaMetadataMapper.TryParseYear(item.ReleaseInfo, out int parsedYear) ? parsedYear : null;
        IReadOnlyList<string> genres = item.Genre ?? [];

        return new CatalogMovie(item.Id, item.Name, year, item.Poster, genres);
    }
}
