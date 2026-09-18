using System.Text.Json;

namespace StrmManager.Modules.Catalog.Infrastructure.Metadata.Cinemeta;

/// <summary>
/// Recognizes Cinemeta's HTTP-200 "this id does not exist" responses. The two media types
/// answer differently and each is recognized ONLY by the shape actually observed live for
/// that type - a shape seen for one type is deliberately not assumed for the other:
///
///   series  unknown id -> an empty top-level object:            {}
///   movie   unknown id -> a meta object with exactly id, type ("movie") and behaviorHints:
///                         {"meta":{"id":"tt...","type":"movie","behaviorHints":{...}}}
///
/// Anything else that lacks usable data - including the other type's stub shape - is a
/// malformed response (InvalidResponse), not a NotFound. Strictness is intentional: a
/// variation that is not recognized fails safe as InvalidResponse rather than being
/// guessed into NotFound. This is provider response interpretation, so it lives here and
/// not in the mapper (which stays strict) or above Infrastructure.
/// </summary>
internal static class CinemetaUnknownMedia
{
    /// <summary>Unknown series id: HTTP 200 with `{}` - no "meta" property and no other top-level property at all.</summary>
    public static bool IsUnknownSeries(CinemetaMetaResponseDto response) =>
        !response.MetaPropertyPresent && response.ExtensionData is not { Count: > 0 };

    /// <summary>
    /// Unknown movie id: a meta object carrying an id and exactly the properties type
    /// ("movie") and behaviorHints - no name/year/release/runtime/status/videos, no other
    /// property, and nothing else at the top level.
    /// </summary>
    public static bool IsUnknownMovie(CinemetaMetaResponseDto response)
    {
        if (response.ExtensionData is { Count: > 0 } || response.Meta is not { } meta)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(meta.Id) ||
            !string.IsNullOrWhiteSpace(meta.Name) ||
            !string.IsNullOrWhiteSpace(meta.ReleaseInfo) ||
            !string.IsNullOrWhiteSpace(meta.Released) ||
            !string.IsNullOrWhiteSpace(meta.Runtime) ||
            !string.IsNullOrWhiteSpace(meta.Status) ||
            meta.Videos is { Count: > 0 })
        {
            return false;
        }

        Dictionary<string, JsonElement>? extra = meta.ExtensionData;

        return extra is { Count: 2 } &&
            extra.TryGetValue("type", out JsonElement type) &&
            type.ValueKind == JsonValueKind.String &&
            string.Equals(type.GetString(), "movie", StringComparison.Ordinal) &&
            extra.ContainsKey("behaviorHints");
    }
}
