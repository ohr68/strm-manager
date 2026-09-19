using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Abstractions.Data;
using StrmManager.Modules.Catalog.Domain.SourceAttempts;
using StrmManager.Modules.Catalog.Infrastructure.Database;
using StrmManager.Modules.Catalog.IntegrationTests.Infrastructure;
using StrmManager.Modules.MediaProcessing.Application.StrmGeneration;
using StrmManager.Modules.MediaProcessing.Application.Streams;
using StrmManager.Modules.MediaProcessing.Application.Validation;

namespace StrmManager.Modules.Catalog.IntegrationTests.Processing;

/// <summary>
/// A ProcessMovie test host: an isolated API over the class's shared database, with a fake
/// stream provider and media validator, a fake clock, the spying unit of work, and the REAL
/// FileSystemStrmWriter (writing into the factory's throwaway temp root) wrapped so its calls appear
/// in the same ordered event log as everything else. Every log line the app emits is captured, so a
/// test can prove a secret (a URL, a header value) never reached a log.
/// </summary>
internal sealed class ProcessMovieHost
{
    public HttpClient Client { get; }

    public IServiceProvider Services { get; }

    public FakeTimeProvider Time { get; } = new(ProcessMovieTestSupport.Now);

    /// <summary>Ordered, thread-safe log: claim/plain saves plus "external:*" events from the fakes and the writer.</summary>
    public ConcurrentQueue<string> Events { get; } = new();

    public ConcurrentQueue<(string CandidateName, MediaValidationReference Reference)> Validated { get; } = new();

    public CapturingLoggerProvider Logs { get; } = new();

    /// <summary>The directory .strm files are written under (the factory's per-instance temp root).</summary>
    public string StrmRoot { get; }

    /// <summary>What the stream provider returns for a movie lookup. Defaults to no candidates.</summary>
    public Func<MovieStreamReference, Result<IReadOnlyList<StreamCandidate>>> Provider { get; set; } =
        _ => Result.Success<IReadOnlyList<StreamCandidate>>([]);

    /// <summary>The ffprobe stand-in. Defaults to failing loudly: tests that expect ffprobe to run must say what it returns.</summary>
    public Func<StreamCandidate, MediaValidationResult> Validate { get; set; } =
        _ => throw new InvalidOperationException("The media validator was not expected to be called.");

    /// <param name="configure">Service overrides, applied last.</param>
    /// <param name="configureHost">Host/configuration overrides (e.g. UseSetting), applied before the services - the path a deployment's environment variables take.</param>
    public ProcessMovieHost(ApiWebApplicationFactory factory, Action<IServiceCollection>? configure = null, Action<IWebHostBuilder>? configureHost = null)
    {
        WebApplicationFactory<Program> configured = configureHost is null ? factory : factory.WithWebHostBuilder(configureHost);

        WebApplicationFactory<Program> isolated = configured.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<TimeProvider>(Time);

                var streamProvider = new FakeStreamProvider
                {
                    MovieHandlerAsync = (reference, _) =>
                    {
                        Events.Enqueue("external:stream-provider");
                        return Task.FromResult(Provider(reference));
                    },
                };
                services.AddSingleton<IStreamProvider>(streamProvider);

                services.AddSingleton<IMediaValidator>(new FakeMediaValidator
                {
                    Handler = (candidate, reference) =>
                    {
                        Events.Enqueue("external:media-validator");
                        Validated.Enqueue((candidate.Name, reference));
                        Time.Advance(TimeSpan.FromSeconds(1)); // distinct, increasing SourceAttempt timestamps
                        return Validate(candidate);
                    },
                });

                // Keep the REAL writer, but log every call it receives.
                ServiceDescriptor realWriter = services.Single(d => d.ServiceType == typeof(IStrmWriter));
                services.Remove(realWriter);
                services.AddSingleton<IStrmWriter>(sp => new EventingStrmWriter(
                    (IStrmWriter)ActivatorUtilities.CreateInstance(sp, realWriter.ImplementationType!), Events));

                services.AddScoped<IUnitOfWork>(sp => new SpyUnitOfWork(sp.GetRequiredService<CatalogDbContext>(), Events));

                services.AddLogging(logging =>
                {
                    logging.SetMinimumLevel(LogLevel.Trace);
                    logging.AddProvider(Logs);
                });

                configure?.Invoke(services);
            }));

        Client = isolated.CreateClient();
        Services = isolated.Services;
        StrmRoot = isolated.Services.GetRequiredService<IConfiguration>()["Strm:RootPath"]
            ?? throw new InvalidOperationException("Strm:RootPath is not configured for the test host.");
    }

    public async Task<(HttpResponseMessage Response, string Body)> ProcessAsync(Guid movieId)
    {
        HttpResponseMessage response = await Client.PostAsync($"/api/movies/{movieId}/process", content: null);
        return (response, await response.Content.ReadAsStringAsync());
    }

    public async Task<IReadOnlyList<SourceAttempt>> AttemptsAsync(Guid movieId)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISourceAttemptRepository>().GetForMovieAsync(movieId);
    }

    /// <summary>Wraps a writer so each call shows up in the event log (the content is never recorded).</summary>
    private sealed class EventingStrmWriter(IStrmWriter inner, ConcurrentQueue<string> events) : IStrmWriter
    {
        public Task<Result<string>> WriteEpisodeAsync(EpisodeStrmReference reference, string sourceUrl, CancellationToken cancellationToken = default)
        {
            events.Enqueue("external:strm-writer");
            return inner.WriteEpisodeAsync(reference, sourceUrl, cancellationToken);
        }

        public Task<Result<string>> WriteMovieAsync(MovieStrmReference reference, string sourceUrl, CancellationToken cancellationToken = default)
        {
            events.Enqueue("external:strm-writer");
            return inner.WriteMovieAsync(reference, sourceUrl, cancellationToken);
        }
    }
}

/// <summary>Collects everything logged - message, category and any exception text - so tests can search it for secrets.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lines = new();

    public IReadOnlyCollection<string> Lines => _lines.ToArray();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _lines);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            lines.Enqueue($"{category} [{logLevel}] {formatter(state, exception)} {exception}");
    }
}
