using Jellyfin.Plugin.StrmManager.Configuration;
using Jellyfin.Plugin.StrmManager.StrmManagerClient;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.StrmManager;

/// <summary>Discovered by the server (IPluginServiceRegistrator) only for enabled + supported plugins.</summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // A typed HttpClient. The configure callback re-reads Plugin.Instance.Configuration.BaseUrl each time the
        // factory builds a client (the factory recycles its pooled handler periodically), so an admin changing the
        // base URL is picked up without a full Jellyfin restart. This is the ONLY place static Plugin.Instance
        // access happens for this feature - StrmManagerClient itself takes its HttpClient already configured, so
        // it stays unit-testable without a running plugin/host.
        serviceCollection.AddHttpClient<IStrmManagerClient, StrmManagerClient.StrmManagerClient>(client =>
        {
            if (StrmManagerBaseUrl.TryNormalize(Plugin.Instance?.Configuration.BaseUrl, out Uri? baseUrl))
            {
                client.BaseAddress = baseUrl;
            }

            // If invalid/unset, BaseAddress stays null; StrmManagerClient checks for that and returns
            // MovieLookupResult.Error rather than throwing.
        });
    }
}
