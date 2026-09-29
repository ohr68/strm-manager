using StrmManager.Modules.Catalog.Application.Metadata;

namespace StrmManager.Modules.Catalog.Infrastructure.Metadata.Cinemeta;

internal static class CinemetaCatalogSeriesMapper
{
    public static CatalogSeries? Map(CinemetaCatalogItemDto item)
    {
        if (string.IsNullOrWhiteSpace(item.Id) ||
            string.IsNullOrWhiteSpace(item.Name))
        {
            return null;
        }

        int? year =
            CinemetaMetadataMapper.TryParseYear(
                item.ReleaseInfo,
                out int parsedYear)
                ? parsedYear
                : null;

        IReadOnlyList<string> genres =
            item.Genre ?? [];

        return new CatalogSeries(
            item.Id,
            item.Name,
            year,
            item.Poster,
            genres);
    }
}
