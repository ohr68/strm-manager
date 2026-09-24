using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.StrmManager.Home;

/// <summary>
/// Registers STRM Manager's experimental Home section with Home Screen Sections (HSS).
/// HSS is an optional integration: when it is not installed or its public registration
/// interface cannot be found, STRM Manager continues to operate normally.
/// </summary>
public sealed class HssRegistrationService : IHostedService
{
    private const string HssAssemblyName = "Jellyfin.Plugin.HomeScreenSections";
    private const string HssPluginInterfaceTypeName =
        "Jellyfin.Plugin.HomeScreenSections.PluginInterface";

    private readonly ILogger<HssRegistrationService> _logger;

    public HssRegistrationService(ILogger<HssRegistrationService> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            Assembly? hssAssembly = AssemblyLoadContext.All
                .SelectMany(context => context.Assemblies)
                .FirstOrDefault(
                    assembly => string.Equals(
                        assembly.GetName().Name,
                        HssAssemblyName,
                        StringComparison.Ordinal));

            if (hssAssembly is null)
            {
                _logger.LogInformation(
                    "Home Screen Sections is not loaded; STRM Manager Home integration is disabled.");

                return Task.CompletedTask;
            }

            Type? pluginInterface = hssAssembly.GetType(
                HssPluginInterfaceTypeName,
                throwOnError: false,
                ignoreCase: false);

            MethodInfo? registerSection = pluginInterface?.GetMethod(
                "RegisterSection",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: [typeof(JObject)],
                modifiers: null);

            if (registerSection is null)
            {
                _logger.LogWarning(
                    "Home Screen Sections is loaded, but RegisterSection(JObject) was not found.");

                return Task.CompletedTask;
            }

            Type resultsType = typeof(HssTestSection);

            var section = new JObject
            {
                ["id"] = "strm-manager-test",
                ["displayText"] = "STRM Manager - Teste",
                ["limit"] = 1,
                ["route"] = string.Empty,
                ["additionalData"] = "ui-9.6a",
                ["resultsAssembly"] = resultsType.Assembly.GetName().Name,
                ["resultsClass"] = resultsType.FullName,
                ["resultsMethod"] = nameof(HssTestSection.GetResults),
            };

            registerSection.Invoke(null, [section]);

            _logger.LogInformation(
                "Registered experimental STRM Manager Home section with Home Screen Sections.");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to register STRM Manager Home section with Home Screen Sections.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
