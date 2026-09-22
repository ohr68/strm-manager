using Jellyfin.Plugin.StrmManager.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.StrmManager;

/// <summary>
/// STRM Manager Jellyfin plugin. P1: the plugin exists and can look a movie up in STRM Manager by IMDb id
/// (see StrmManagerClient/, Api/MoviesController). No add/process/scan/background work yet - see the P1 report.
/// </summary>
public sealed class Plugin : BasePlugin<PluginConfiguration>
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>
    /// The running plugin instance, set by the host when it constructs this class. Only the DI registration in
    /// <see cref="PluginServiceRegistrator"/> reads this (to seed the STRM Manager HTTP client's base address) -
    /// application code that needs the configured base URL should take it through DI, not through this static,
    /// so it stays unit-testable without a running Jellyfin host.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    public override string Name => "STRM Manager";

    public override Guid Id => Guid.Parse("2f2a7c3f-6b3e-4c1a-9f0a-2b6a5f6d9c2a");

    public override string Description => "Looks up and (in later slices) provisions movies in STRM Manager from Jellyfin.";
}
