using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.StrmManager.Configuration;

/// <summary>
/// Persisted plugin configuration. P1 only needs the STRM Manager base URL - no credentials, no auth (matching the
/// accepted MVP architecture: the plugin does no provider work and, in this slice, no write operations either).
/// </summary>
public sealed class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// STRM Manager's base URL, e.g. "http://strm-manager-vnext-e2e:8080". Must be validated with
    /// <see cref="StrmManagerBaseUrl.TryNormalize"/> before use - this property is the raw, admin-entered value and
    /// may be empty, malformed, or otherwise unsafe to use as-is. Never inferred from the incoming request's Host.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// The Jellyfin movie library STRM Manager writes movie .strm files into - the persisted value is
    /// VirtualFolderInfo.ItemId (a stable string id for the library's underlying Folder item), never a filesystem
    /// path, so a library rename/relocation doesn't break the selection. Null/empty until an admin picks one on the
    /// configuration page (see UI-1 / Api/LibrariesController). Not yet consumed by any processing code.
    /// </summary>
    public string? MoviesLibraryId { get; set; }

    /// <summary>Same as <see cref="MoviesLibraryId"/>, for the series library.</summary>
    public string? SeriesLibraryId { get; set; }
}
