using Jellyfin.Plugin.StrmManager.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.StrmManager;

/// <summary>
/// STRM Manager Jellyfin plugin. UI-1 adds the plugin's admin configuration page (backend URL, Movies/Series
/// library selection) - see Web/configurationpage.html and Api/LibrariesController. No catalog/watch UI yet.
/// </summary>
public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
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

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = "STRM Manager",
            DisplayName = "STRM Manager",
            EmbeddedResourcePath = $"{GetType().Namespace}.Web.configurationpage.html",
            EnableInMainMenu = true,
            MenuSection = "server",
            MenuIcon = "video_library",
        };

        // UI-2: a separate page from configuration - browsing and configuring are different concerns (see
        // Web/discover.html).
        yield return new PluginPageInfo
        {
            Name = "StrmManagerDiscover",
            DisplayName = "STRM Manager Discover",
            EmbeddedResourcePath = $"{GetType().Namespace}.Web.discover.html",
            EnableInMainMenu = true,
            MenuSection = "server",
            MenuIcon = "movie",
        };
    }
}
