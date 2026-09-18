using System.Text.Json;
using System.Text.Json.Serialization;

namespace StrmManager.Modules.Catalog.Infrastructure.Metadata.Cinemeta;

/// <summary>
/// Raw Cinemeta/Stremio addon response shape - the same envelope for both
/// GET /meta/series/{imdbId}.json and GET /meta/movie/{imdbId}.json. Never exposed
/// outside Infrastructure - CinemetaMetadataMapper converts this into the
/// provider-neutral SeriesMetadata / MovieMetadata (StrmManager.Modules.Catalog.Application.Metadata).
///
/// Cinemeta answers HTTP 200 (not 404) for ids it does not know, and the shape differs
/// per media type. This DTO only records the raw structure (what was present, what was
/// not mapped); deciding what counts as "unknown media" is CinemetaUnknownMedia's job,
/// per media type.
/// </summary>
internal sealed class CinemetaMetaResponseDto
{
    private CinemetaMetaDto? _meta;

    /// <summary>The setter also records that "meta" was present, so `{}` (absent) can be told apart from `{"meta":null}` (present but null).</summary>
    public CinemetaMetaDto? Meta
    {
        get => _meta;
        set
        {
            _meta = value;
            MetaPropertyPresent = true;
        }
    }

    /// <summary>Any top-level property other than "meta".</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    [JsonIgnore]
    public bool MetaPropertyPresent { get; private set; }
}

internal sealed class CinemetaMetaDto
{
    /// <summary>Properties this DTO does not map (e.g. "type", "behaviorHints", "poster").</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public string? Id { get; set; }

    public string? Name { get; set; }

    /// <summary>Series: e.g. "2026-" (ongoing) or "2020-2023" (ended). Movies: e.g. "1994".</summary>
    public string? ReleaseInfo { get; set; }

    /// <summary>Movies only: ISO timestamp of the release, e.g. "1994-10-14T00:00:00.000Z". Absent when Cinemeta has no date.</summary>
    public string? Released { get; set; }

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
