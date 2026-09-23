using System.Text.Json;
using System.Text.Json.Serialization;

namespace StrmManager.Modules.Catalog.Infrastructure.Metadata.Cinemeta;

/// <summary>
/// Raw Cinemeta/Stremio catalog response shape - GET catalog/movie/top.json ("Popular"). Verified live against
/// v3-cinemeta.strem.io before implementation (see the UI-2 report): a top-level {"metas": [...]} envelope, each
/// item carrying far more than this DTO maps (cast, director, trailers, ratings, etc.) - deliberately only the
/// fields the catalog row needs are declared; the rest is never exposed outside Infrastructure
/// (CinemetaCatalogMapper converts this into the provider-neutral CatalogMovie).
/// </summary>
internal sealed class CinemetaCatalogResponseDto
{
    public List<CinemetaCatalogItemDto>? Metas { get; set; }
}

internal sealed class CinemetaCatalogItemDto
{
    /// <summary>Everything this DTO does not map (cast, director, trailers, ratings, genres, etc.).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public string? Id { get; set; }

    public string? Name { get; set; }

    /// <summary>e.g. "2026" - same format as CinemetaMetaDto.ReleaseInfo, parsed by the same CinemetaMetadataMapper.TryParseYear rule.</summary>
    public string? ReleaseInfo { get; set; }

    /// <summary>A ready-to-use absolute image URL (e.g. https://images.metahub.space/poster/small/{imdbId}/img) - verified live, never built/guessed client-side.</summary>
    public string? Poster { get; set; }

    /// <summary>Cinemeta's own genre tags for this item (e.g. "Action", "Comedy") - used by UI-4's row policy (see CinemetaCatalogMapper/CatalogMovie.Genres). Never aliased or inferred - only what Cinemeta itself reports.</summary>
    public List<string>? Genre { get; set; }
}
