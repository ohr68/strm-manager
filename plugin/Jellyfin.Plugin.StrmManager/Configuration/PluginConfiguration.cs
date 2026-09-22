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
}
