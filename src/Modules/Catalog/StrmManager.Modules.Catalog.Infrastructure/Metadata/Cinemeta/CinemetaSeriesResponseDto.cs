namespace StrmManager.Modules.Catalog.Infrastructure.Metadata.Cinemeta;

/// <summary>
/// Raw Cinemeta/Stremio addon response shape (GET /meta/series/{imdbId}.json). Never
/// exposed outside Infrastructure - CinemetaMetadataMapper converts this into the
/// provider-neutral StrmManager.Modules.Catalog.Application.Metadata.SeriesMetadata.
/// </summary>
internal sealed class CinemetaSeriesResponseDto
{
    public CinemetaMetaDto? Meta { get; set; }
}

internal sealed class CinemetaMetaDto
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    /// <summary>e.g. "2026-" (ongoing) or "2020-2023" (ended).</summary>
    public string? ReleaseInfo { get; set; }

    /// <summary>e.g. "20 min". Free-form and not always present.</summary>
    public string? Runtime { get; set; }

    /// <summary>e.g. "Continuing", "Ended". Not a closed/reliable enum on Cinemeta's side.</summary>
    public string? Status { get; set; }

    public List<CinemetaVideoDto>? Videos { get; set; }
}

internal sealed class CinemetaVideoDto
{
    public string? Id { get; set; }

    public string? Title { get; set; }

    public int? Season { get; set; }

    /// <summary>Primary episode-number field per the Stremio addon protocol.</summary>
    public int? Number { get; set; }

    /// <summary>Sometimes present alongside Number with the same value - fallback only.</summary>
    public int? Episode { get; set; }

    public string? FirstAired { get; set; }

    public string? Released { get; set; }
}
