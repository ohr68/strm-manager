using System.Text.Json;
using StrmManager.Modules.MediaProcessing.Application.Streams;

namespace StrmManager.Modules.MediaProcessing.Infrastructure.Streams.FrostStream;

internal static class FrostStreamMapper
{
    private const string ProviderName = "FrostStream";

    // The one bingeGroup form observed to carry a real identity: MegaEmbed's TMDB id. Cloutstream
    // bingeGroups ("cloutstream-cs:movie:{localized-title-slug}") are title slugs - not identity -
    // and any other format is unknown, so it means nothing.
    private const string MegaEmbedMoviePrefix = "megaembed-movie-";
    private const int MaxTmdbIdDigits = 9;

    /// <summary>
    /// Only http/https candidates are kept - defends against a provider ever returning a
    /// non-network URI (file://, etc.) that later code might mishandle (see ADR on the
    /// MediaProcessing trust boundary). A candidate with no name/URL at all is dropped.
    /// </summary>
    public static IReadOnlyList<StreamCandidate> Map(FrostStreamResponseDto response)
    {
        if (response.Streams is null)
        {
            return [];
        }

        List<StreamCandidate> candidates = [];

        foreach (FrostStreamStreamDto stream in response.Streams)
        {
            if (string.IsNullOrWhiteSpace(stream.Url) || string.IsNullOrWhiteSpace(stream.Name))
            {
                continue;
            }

            if (!Uri.TryCreate(stream.Url, UriKind.Absolute, out Uri? uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                continue;
            }

            candidates.Add(new StreamCandidate(
                ProviderName,
                stream.Name,
                stream.Title,
                stream.Url,
                ReadTmdbId(stream.BehaviorHints),
                RequiresCustomHeaders(stream.Headers)));
        }

        return candidates;
    }

    /// <summary>
    /// A TMDB id, but only from a bingeGroup that is exactly "megaembed-movie-{1-9 ASCII digits}".
    /// Anything else - other providers' groups, other shapes, a non-string value - yields null.
    /// </summary>
    private static string? ReadTmdbId(JsonElement? behaviorHints)
    {
        if (behaviorHints is not { ValueKind: JsonValueKind.Object } hints ||
            !hints.TryGetProperty("bingeGroup", out JsonElement bingeGroup) ||
            bingeGroup.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? value = bingeGroup.GetString();

        if (value is null || !value.StartsWith(MegaEmbedMoviePrefix, StringComparison.Ordinal))
        {
            return null;
        }

        ReadOnlySpan<char> digits = value.AsSpan(MegaEmbedMoviePrefix.Length);

        if (digits.Length is < 1 or > MaxTmdbIdDigits)
        {
            return null;
        }

        foreach (char digit in digits)
        {
            if (digit is < '0' or > '9')
            {
                return null;
            }
        }

        return digits.ToString();
    }

    /// <summary>
    /// True when the entry carries anything in "headers" (custom request headers such as
    /// User-Agent/Referer/Origin). Only presence is reported - a value is never read. An absent,
    /// null, empty-object or empty-array value means none; any other non-empty shape is treated as
    /// "required" (the safe reading: such a stream is then not used).
    /// </summary>
    private static bool RequiresCustomHeaders(JsonElement? headers) =>
        headers is { } element && element.ValueKind switch
        {
            JsonValueKind.Undefined or JsonValueKind.Null => false,
            JsonValueKind.Object => element.EnumerateObject().MoveNext(),
            JsonValueKind.Array => element.GetArrayLength() > 0,
            _ => true,
        };
}
