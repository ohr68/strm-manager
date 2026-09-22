namespace Jellyfin.Plugin.StrmManager.Configuration;

/// <summary>
/// Validates and normalizes the admin-entered STRM Manager base URL. Deliberately narrow (mirrors the backend's own
/// Playback:PublicBaseUrl rule): absolute, http/https only, no userinfo, no query, no fragment. Never derives a base
/// URL from anything else (e.g. the incoming request's Host) - an invalid/missing configuration must simply fail,
/// not silently guess at a value.
/// </summary>
public static class StrmManagerBaseUrl
{
    /// <summary>
    /// Normalizes <paramref name="rawBaseUrl"/> to an absolute URI usable as <see cref="HttpClient.BaseAddress"/>
    /// (ending in "/", so a relative request path like "api/movies/by-imdb/tt123" appends rather than replaces the
    /// last segment). Returns false - with <paramref name="normalized"/> null - for anything not exactly right.
    /// </summary>
    public static bool TryNormalize(string? rawBaseUrl, out Uri? normalized)
    {
        normalized = null;

        if (string.IsNullOrWhiteSpace(rawBaseUrl))
        {
            return false;
        }

        if (!Uri.TryCreate(rawBaseUrl, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        string path = uri.GetLeftPart(UriPartial.Path);
        normalized = new Uri(path.EndsWith('/') ? path : path + "/", UriKind.Absolute);
        return true;
    }
}
