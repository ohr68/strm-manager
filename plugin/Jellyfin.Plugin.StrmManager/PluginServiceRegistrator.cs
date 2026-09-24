using Jellyfin.Plugin.StrmManager.Configuration;
using Jellyfin.Plugin.StrmManager.Home;
using Jellyfin.Plugin.StrmManager.Movies;
using Jellyfin.Plugin.StrmManager.Processing;
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
        // The ordinary typed HttpClient, used for the fast metadata calls (GetByImdbIdAsync/AddMovieAsync) - keeps
        // the framework's default ~100s timeout, unchanged.
        serviceCollection.AddHttpClient<IStrmManagerClient, StrmManagerClient.StrmManagerClient>(ConfigureBaseUrl);

        // A dedicated named client for the long-running ProcessMovie POST only - same BaseUrl rules as the ordinary
        // typed client above (via the same ConfigureBaseUrl callback), but with no client-side timeout: that call
        // can legitimately run far longer than 100s, and its lifetime should be governed only by the caller's own
        // CancellationToken (the P5 worker's host stopping token), not an arbitrary framework default meant for
        // fast metadata calls. GetByImdbIdAsync/AddMovieAsync never use this client.
        serviceCollection.AddHttpClient(StrmManagerClient.StrmManagerClient.ProcessMovieHttpClientName, client =>
        {
            ConfigureBaseUrl(client);
            client.Timeout = Timeout.InfiniteTimeSpan;
        });

        // Pure composition over IStrmManagerClient (no HttpClient of its own) - stateless, so transient is fine.
        serviceCollection.AddTransient<IEnsureMovieService, EnsureMovieService>();

        // P5: Process Movie background queue infrastructure. Both registrations resolve the SAME singleton
        // instance - ProcessMovieWorker (constructor-injecting the concrete type, since it needs the internal
        // Reader/Release members) and any future producer (through IProcessMovieQueue, enqueue-only) always share
        // one queue. No product code calls IProcessMovieQueue yet - see the P5 report; the worker will simply wait
        // on an empty queue.
        serviceCollection.AddSingleton<ProcessMovieQueue>();
        serviceCollection.AddSingleton<IProcessMovieQueue>(sp => sp.GetRequiredService<ProcessMovieQueue>());
        serviceCollection.AddHostedService<ProcessMovieWorker>();

        // UI-9.6A: optional Home Screen Sections integration. The hosted service
        // performs a no-op when HSS is not installed or its registration API is unavailable.
        serviceCollection.AddHostedService<HssRegistrationService>();
    }

    /// <summary>
    /// Re-reads Plugin.Instance.Configuration.BaseUrl each time a client is built (both registrations above share
    /// this) - the factory recycles pooled handlers periodically, and the named ProcessMovie client is created
    /// fresh on every call (see StrmManagerClient.ProcessMovieAsync), so an admin changing the base URL is always
    /// picked up without a full Jellyfin restart. This is the ONLY place static Plugin.Instance access happens for
    /// this feature - StrmManagerClient itself takes its HttpClients already configured, so it stays unit-testable
    /// without a running plugin/host.
    /// </summary>
    private static void ConfigureBaseUrl(HttpClient client)
    {
        if (StrmManagerBaseUrl.TryNormalize(Plugin.Instance?.Configuration.BaseUrl, out Uri? baseUrl))
        {
            client.BaseAddress = baseUrl;
        }

        // If invalid/unset, BaseAddress stays null; StrmManagerClient checks for that and returns an Error result
        // rather than throwing.
    }
}
