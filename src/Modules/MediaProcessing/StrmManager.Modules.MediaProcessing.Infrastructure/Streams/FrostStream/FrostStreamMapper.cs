using StrmManager.Modules.MediaProcessing.Application.Streams;

namespace StrmManager.Modules.MediaProcessing.Infrastructure.Streams.FrostStream;

internal static class FrostStreamMapper
{
    private const string ProviderName = "FrostStream";

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

            candidates.Add(new StreamCandidate(ProviderName, stream.Name, stream.Title, stream.Url));
        }

        return candidates;
    }
}
