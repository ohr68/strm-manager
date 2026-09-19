using FluentValidation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using StrmManager.Common.Application.Messaging;
using StrmManager.Modules.Catalog.Application.Abstractions.Data;
using StrmManager.Modules.Catalog.Application.Metadata;
using StrmManager.Modules.Catalog.Application.Playback;
using StrmManager.Modules.Catalog.Application.Processing;
using StrmManager.Modules.Catalog.Application.Series.AddSeries;
using StrmManager.Modules.Catalog.Application.Status;
using StrmManager.Modules.Catalog.Domain.Episodes;
using StrmManager.Modules.Catalog.Domain.Movies;
using StrmManager.Modules.Catalog.Domain.Seasons;
using StrmManager.Modules.Catalog.Domain.SourceAttempts;
using StrmManager.Modules.Catalog.Domain.StrmFiles;
using StrmManager.Modules.Catalog.Infrastructure.Database;
using StrmManager.Modules.Catalog.Infrastructure.Episodes;
using StrmManager.Modules.Catalog.Infrastructure.Metadata.Cinemeta;
using StrmManager.Modules.Catalog.Infrastructure.Movies;
using StrmManager.Modules.Catalog.Infrastructure.Seasons;
using StrmManager.Modules.Catalog.Infrastructure.SourceAttempts;
using StrmManager.Modules.Catalog.Infrastructure.StrmFiles;
using ISeriesRepository = StrmManager.Modules.Catalog.Domain.Series.ISeriesRepository;
using SeriesRepository = StrmManager.Modules.Catalog.Infrastructure.Series.SeriesRepository;

namespace StrmManager.Modules.Catalog.Infrastructure;

public static class CatalogModule
{
    public static IServiceCollection AddCatalogModule(this IServiceCollection services, IConfiguration configuration)
    {
        string connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("The 'Database' connection string is not configured.");

        // Microsoft.Data.Sqlite does not enforce FK constraints unless explicitly asked
        // to - without this, the Restrict/cascade behaviors configured on the
        // Series/Season/Episode relationships would be silently ignored at runtime.
        var sqliteConnectionStringBuilder = new SqliteConnectionStringBuilder(connectionString)
        {
            ForeignKeys = true,
            // Now that the API host and two Scheduling BackgroundServices can genuinely
            // overlap (Phase 4), a brief SQLITE_BUSY on a concurrent write is expected
            // occasionally - retry internally for up to 30s rather than surfacing it as
            // an immediate failure. See ADR-014 (WAL + busy-timeout decision).
            DefaultTimeout = 30,
        };

        services.AddDbContext<CatalogDbContext>(options => options.UseSqlite(sqliteConnectionStringBuilder.ConnectionString));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CatalogDbContext>());

        services.AddScoped<ISeriesRepository, SeriesRepository>();
        services.AddScoped<ISeasonRepository, SeasonRepository>();
        services.AddScoped<IEpisodeRepository, EpisodeRepository>();
        services.AddScoped<IMovieRepository, MovieRepository>();
        services.AddScoped<ISourceAttemptRepository, SourceAttemptRepository>();
        services.AddScoped<IStrmFileRepository, StrmFileRepository>();

        services.AddScoped<CatalogSynchronizer>();

        // Read-only, just-in-time playback resolution (ADR-015). No endpoint uses it yet.
        services.AddScoped<IPlaybackResolver, PlaybackResolver>();

        // Single-flight + bounded concurrency around the resolver. A SINGLETON on purpose: the in-flight state must be
        // shared by every request. It therefore takes no scoped dependency - it gets the root scope factory and creates
        // (and disposes) its own scope for each shared resolution - and stops its work when the host begins to stop.
        services.AddSingleton<IPlaybackResolutionCoordinator>(serviceProvider => new PlaybackResolutionCoordinator(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            serviceProvider.GetRequiredService<IOptions<PlaybackResolutionOptions>>(),
            serviceProvider.GetRequiredService<TimeProvider>(),
            serviceProvider.GetRequiredService<ILogger<PlaybackResolutionCoordinator>>(),
            serviceProvider.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping));

        services.AddHandlersFromAssembly(typeof(AddSeriesCommand).Assembly);
        services.AddValidatorsFromAssembly(typeof(AddSeriesCommand).Assembly, includeInternalTypes: true);

        services.AddCinemetaMetadataProvider(configuration);

        services.AddOptions<ProcessingOptions>()
            .Bind(configuration.GetSection(ProcessingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Every value has a valid default, so a deployment with no "Playback:Resolution" section starts exactly as before.
        services.AddOptions<PlaybackResolutionOptions>()
            .Bind(configuration.GetSection(PlaybackResolutionOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<MetadataRefreshOptions>()
            .Bind(configuration.GetSection(MetadataRefreshOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // No [Required]/[Range] attributes - Commit is legitimately null outside Docker
        // (e.g. `dotnet run` in dev), not a misconfiguration. See BuildInfoOptions.
        services.AddOptions<BuildInfoOptions>()
            .Bind(configuration.GetSection(BuildInfoOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Safe fallback if nothing else registers ISchedulerStatusProvider (e.g. a test
        // host that never adds the Scheduling module) - Scheduling.Infrastructure's own
        // registration is added after this one in Program.cs and wins for resolution.
        services.TryAddSingleton<ISchedulerStatusProvider, DisabledSchedulerStatusProvider>();

        return services;
    }

    private sealed class DisabledSchedulerStatusProvider : ISchedulerStatusProvider
    {
        public bool Enabled => false;
    }

    private static void AddCinemetaMetadataProvider(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CinemetaOptions>()
            .Bind(configuration.GetSection(CinemetaOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<IMetadataProvider, CinemetaMetadataProvider>((sp, client) =>
            {
                CinemetaOptions options = sp.GetRequiredService<IOptions<CinemetaOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl);
            })
            // One layer owns HTTP resilience (here) - CinemetaMetadataProvider itself
            // never retries. Conservative on purpose: a handful of retries with a short
            // overall timeout, and 4xx (including 404 "series not found") is never
            // retried by the default ShouldHandle predicate - it isn't transient.
            .AddResilienceHandler("cinemeta-metadata", (builder, context) =>
            {
                CinemetaOptions options = context.ServiceProvider.GetRequiredService<IOptions<CinemetaOptions>>().Value;

                builder.AddTimeout(TimeSpan.FromSeconds(options.TimeoutSeconds));
                builder.AddRetry(new HttpRetryStrategyOptions
                {
                    // HttpRetryStrategyOptions' default ShouldHandle already targets
                    // only transient outcomes (5xx, 408, network/timeout failures) -
                    // 404 "not found" and other 4xx responses are never retried.
                    MaxRetryAttempts = 2,
                    BackoffType = DelayBackoffType.Exponential,
                    Delay = TimeSpan.FromMilliseconds(200),
                    UseJitter = true,
                });
            });
    }
}
