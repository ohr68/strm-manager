using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using StrmManager.Modules.MediaProcessing.Application.StrmGeneration;
using StrmManager.Modules.MediaProcessing.Application.Streams;
using StrmManager.Modules.MediaProcessing.Application.Validation;
using StrmManager.Modules.MediaProcessing.Infrastructure.Streams.FrostStream;
using StrmManager.Modules.MediaProcessing.Infrastructure.StrmGeneration;
using StrmManager.Modules.MediaProcessing.Infrastructure.Validation.Ffprobe;

namespace StrmManager.Modules.MediaProcessing.Infrastructure;

public static class MediaProcessingModule
{
    public static IServiceCollection AddMediaProcessingModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<StrmOptions>()
            .Bind(configuration.GetSection(StrmOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<FfprobeOptions>()
            .Bind(configuration.GetSection(FfprobeOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IStrmWriter, FileSystemStrmWriter>();
        services.AddSingleton<IMediaValidator, FfprobeMediaValidator>();

        services.AddOptions<FrostStreamOptions>()
            .Bind(configuration.GetSection(FrostStreamOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<IStreamProvider, FrostStreamProvider>((sp, client) =>
            {
                FrostStreamOptions options = sp.GetRequiredService<IOptions<FrostStreamOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl);
            })
            // Same conservative policy as Cinemeta (ADR-007): a short timeout, a couple
            // of retries on transient failures only - never on permanent 4xx responses.
            .AddResilienceHandler("froststream", (builder, context) =>
            {
                FrostStreamOptions options = context.ServiceProvider.GetRequiredService<IOptions<FrostStreamOptions>>().Value;

                builder.AddTimeout(TimeSpan.FromSeconds(options.TimeoutSeconds));
                builder.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 2,
                    BackoffType = DelayBackoffType.Exponential,
                    Delay = TimeSpan.FromMilliseconds(200),
                    UseJitter = true,
                });
            });

        return services;
    }
}
