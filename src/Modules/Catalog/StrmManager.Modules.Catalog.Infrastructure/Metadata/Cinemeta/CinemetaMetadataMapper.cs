using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Metadata;
using StrmManager.Modules.Catalog.Domain.Series;
using StrmManager.Modules.Catalog.Domain.Shared;

namespace StrmManager.Modules.Catalog.Infrastructure.Metadata.Cinemeta;

/// <summary>
/// Converts the raw Cinemeta DTO into provider-neutral SeriesMetadata / MovieMetadata.
/// Deliberately maps only the fields Catalog currently uses (see ADR-007) - not
/// Cinemeta's full schema.
/// </summary>
internal static partial class CinemetaMetadataMapper
{
    private const string ProviderName = "Cinemeta";

    public static Result<SeriesMetadata> Map(CinemetaMetaDto meta)
    {
        if (string.IsNullOrWhiteSpace(meta.Name))
        {
            return Result.Failure<SeriesMetadata>(MetadataProviderErrors.InvalidResponse(ProviderName, "missing series name"));
        }

        if (!TryParseYear(meta.ReleaseInfo, out int year))
        {
            return Result.Failure<SeriesMetadata>(MetadataProviderErrors.InvalidResponse(ProviderName, "missing or invalid release year"));
        }

        SeriesStatus status = MapStatus(meta.Status);
        TimeSpan? runtime = ParseRuntimeMinutes(meta.Runtime);

        List<EpisodeMetadata> episodes = (meta.Videos ?? [])
            .Select(video => MapEpisode(video, runtime))
            .Where(episode => episode is not null)
            .Select(episode => episode!)
            .ToList();

        var externalIds = new ExternalIds(meta.Id, null, null);

        return new SeriesMetadata(externalIds, meta.Name, null, year, status, episodes);
    }

    /// <summary>
    /// Maps a Cinemeta movie meta object. The provider id becomes the canonical IMDb id
    /// (the duplicate key), so a missing id is a failure, not a null. The release date is
    /// optional at this boundary - a missing/unparsable one stays null and is never
    /// invented (AddMovie decides what to do about it).
    /// </summary>
    public static Result<MovieMetadata> MapMovie(CinemetaMetaDto meta)
    {
        if (string.IsNullOrWhiteSpace(meta.Id))
        {
            return Result.Failure<MovieMetadata>(MetadataProviderErrors.InvalidResponse(ProviderName, "missing movie id"));
        }

        if (string.IsNullOrWhiteSpace(meta.Name))
        {
            return Result.Failure<MovieMetadata>(MetadataProviderErrors.InvalidResponse(ProviderName, "missing movie name"));
        }

        if (!TryParseYear(meta.ReleaseInfo, out int year))
        {
            return Result.Failure<MovieMetadata>(MetadataProviderErrors.InvalidResponse(ProviderName, "missing or invalid release year"));
        }

        DateTime? releaseAtUtc = TryParseUtc(meta.Released, out DateTime released) ? released : null;

        return new MovieMetadata(
            new ExternalIds(meta.Id, ReadTmdbId(meta.ExtensionData), null),
            meta.Name,
            year,
            ParseRuntimeMinutes(meta.Runtime),
            releaseAtUtc);
    }

    private const string TmdbIdProperty = "moviedb_id";
    private const long MaxTmdbId = 9_999_999_999;

    /// <summary>
    /// Cinemeta's TMDB id ("moviedb_id") for a movie. Read from the DTO's extension data - it is
    /// deliberately not a declared DTO property, so the unknown-media stub detection (which treats
    /// any extra property as "not the stub") is unaffected. Optional and tolerant: a missing value,
    /// or one that is not a positive integer (number or ASCII-digit string), is simply null - a bad
    /// optional id must never stop a movie being added.
    /// </summary>
    private static string? ReadTmdbId(Dictionary<string, JsonElement>? extensionData)
    {
        if (extensionData is null || !extensionData.TryGetValue(TmdbIdProperty, out JsonElement value))
        {
            return null;
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.Number:
                return value.TryGetInt64(out long number) && number is > 0 and <= MaxTmdbId
                    ? number.ToString(CultureInfo.InvariantCulture)
                    : null;

            case JsonValueKind.String:
                string? text = value.GetString();
                return text is { Length: >= 1 and <= 10 } &&
                       text.All(character => character is >= '0' and <= '9') &&
                       long.Parse(text, CultureInfo.InvariantCulture) > 0
                    ? text
                    : null;

            default:
                return null;
        }
    }

    private static EpisodeMetadata? MapEpisode(CinemetaVideoDto video, TimeSpan? seriesRuntime)
    {
        // Season 0 (specials) is preserved by design - see ADR-007. We only need a
        // non-negative season number and a usable episode number/id to synchronize.
        if (video.Season is not { } seasonNumber || seasonNumber < 0)
        {
            return null;
        }

        int? episodeNumber = video.Number ?? video.Episode;

        if (episodeNumber is not { } number || number <= 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(video.Id))
        {
            return null;
        }

        // firstAired preferred over released per ADR-007; neither present -> null,
        // never invented (CatalogSynchronizer skips episodes with no release date).
        DateTime? releaseAtUtc = TryParseUtc(video.FirstAired, out DateTime firstAired)
            ? firstAired
            : TryParseUtc(video.Released, out DateTime released)
                ? released
                : null;

        string title = string.IsNullOrWhiteSpace(video.Title)
            ? $"Episode {number}"
            : video.Title;

        return new EpisodeMetadata(video.Id, title, seasonNumber, number, seriesRuntime, releaseAtUtc);
    }

    private static bool TryParseUtc(string? value, out DateTime utc)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out DateTime parsed))
        {
            utc = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            return true;
        }

        utc = default;
        return false;
    }

    private static bool TryParseYear(string? releaseInfo, out int year)
    {
        if (!string.IsNullOrWhiteSpace(releaseInfo))
        {
            Match match = YearRegex().Match(releaseInfo);

            if (match.Success && int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out year))
            {
                return true;
            }
        }

        year = 0;
        return false;
    }

    private static SeriesStatus MapStatus(string? status) => status?.Trim().ToUpperInvariant() switch
    {
        "ENDED" => SeriesStatus.Ended,
        "CONTINUING" => SeriesStatus.Active,
        // Missing/unrecognized status must not fail synchronization - conservative
        // "assume still airing" default, per ADR-007.
        _ => SeriesStatus.Active,
    };

    private static TimeSpan? ParseRuntimeMinutes(string? runtime)
    {
        if (string.IsNullOrWhiteSpace(runtime))
        {
            return null;
        }

        Match match = RuntimeMinutesRegex().Match(runtime);

        return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int minutes)
            ? TimeSpan.FromMinutes(minutes)
            : null;
    }

    [GeneratedRegex(@"\d{4}")]
    private static partial Regex YearRegex();

    [GeneratedRegex(@"(\d+)\s*min")]
    private static partial Regex RuntimeMinutesRegex();
}
