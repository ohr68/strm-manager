using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StrmManager.Modules.Catalog.Application.Status;

namespace StrmManager.Modules.Scheduling.Infrastructure;

public static class SchedulingModule
{
    public static IServiceCollection AddSchedulingModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SchedulingOptions>()
            .Bind(configuration.GetSection(SchedulingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Registered after CatalogModule's fallback DisabledSchedulerStatusProvider in
        // Program.cs - the last registration wins for a single-instance resolution, so
        // this real implementation is what GetOperationalStatusQueryHandler resolves.
        services.AddSingleton<ISchedulerStatusProvider, SchedulerStatusProvider>();

        services.AddHostedService<CatalogMaintenanceWorker>();
        services.AddHostedService<EpisodeProcessingWorker>();

        return services;
    }
}
