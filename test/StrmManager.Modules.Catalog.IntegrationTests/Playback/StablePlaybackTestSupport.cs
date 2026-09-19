using System.Collections.Concurrent;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using StrmManager.Modules.Catalog.Application.Playback;

namespace StrmManager.Modules.Catalog.IntegrationTests.Playback;

/// <summary>A synthetic provider URL with several distinct sensitive-looking parts - never a real one.</summary>
internal static class Canary
{
    public const string Host = "canary-host-b81d47.invalid";
    public const string PathSecret = "PATHSECRET-3fa2";
    public const string QuerySecret = "QUERYSECRET-c9e6";

    public static readonly string Url = $"https://{Host}/hls/{PathSecret}/master.m3u8?sig={QuerySecret}";

    public static readonly string[] Parts = [Url, Host, PathSecret, QuerySecret];
}

/// <summary>Stands in for the real coordinator so the endpoint's contract can be driven outcome by outcome, with no provider, ffprobe or database.</summary>
internal sealed class ControllableCoordinator : IPlaybackResolutionCoordinator
{
    public ConcurrentQueue<(Guid MovieId, CancellationToken Token)> Calls { get; } = new();

    public Func<Guid, CancellationToken, Task<PlaybackCoordinationResult>> Handler { get; set; } =
        (_, _) => throw new InvalidOperationException("The coordinator was not expected to be called.");

    public void Returns(PlaybackCoordinationResult result) => Handler = (_, _) => Task.FromResult(result);

    public Task<PlaybackCoordinationResult> ResolveAsync(Guid movieId, CancellationToken waiterToken)
    {
        Calls.Enqueue((movieId, waiterToken));
        return Handler(movieId, waiterToken);
    }
}

/// <summary>Everything the app logged, at every level and for every category: message, each structured property, and exception text.</summary>
internal sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _entries = new();

    public IReadOnlyCollection<string> Entries => _entries.ToArray();

    public string AllText => string.Join('\n', _entries);

    public ILogger CreateLogger(string categoryName) => new Recorder(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class Recorder(string category, ConcurrentQueue<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var entry = new StringBuilder();
            entry.Append(category).Append(" [").Append(logLevel).Append("] ").Append(formatter(state, exception));

            if (state is IEnumerable<KeyValuePair<string, object?>> properties)
            {
                foreach (KeyValuePair<string, object?> property in properties)
                {
                    entry.Append(" | ").Append(property.Key).Append('=').Append(property.Value);
                }
            }

            if (exception is not null)
            {
                entry.Append(" | exception=").Append(exception);
            }

            entries.Enqueue(entry.ToString());
        }
    }
}

/// <summary>The outcome of every request as the OUTERMOST middleware saw it - after the exception handler has had its say.</summary>
internal sealed class RequestOutcomeRecorder
{
    public ConcurrentQueue<(string Method, string Path, int Status, string? ExceptionType)> Outcomes { get; } = new();

    public sealed class Filter(RequestOutcomeRecorder recorder) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, pipeline) =>
            {
                string? exceptionType = null;

                try
                {
                    await pipeline(context);
                }
                catch (Exception exception)
                {
                    exceptionType = exception.GetType().Name;
                    throw;
                }
                finally
                {
                    recorder.Outcomes.Enqueue((context.Request.Method, context.Request.Path, context.Response.StatusCode, exceptionType));
                }
            });

            next(app);
        };
    }
}

/// <summary>What a client saw, with the Location header set apart so everything ELSE can be scanned for a secret.</summary>
internal sealed record ObservedResponse(int Status, string? Location, IReadOnlyDictionary<string, string> OtherHeaders, byte[] Body)
{
    public bool HasHeader(string name) => OtherHeaders.ContainsKey(name);

    public string? Header(string name) => OtherHeaders.TryGetValue(name, out string? value) ? value : null;

    /// <summary>Everything except the Location header: the other headers (names and values) and the body.</summary>
    public string EverythingButLocation =>
        string.Join('\n', OtherHeaders.Select(h => $"{h.Key}: {h.Value}").Append(Encoding.UTF8.GetString(Body)));

    public static async Task<ObservedResponse> FromAsync(HttpResponseMessage response)
    {
        string? location = null;
        var others = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (KeyValuePair<string, IEnumerable<string>> header in response.Headers.Concat(response.Content.Headers))
        {
            string value = string.Join(", ", header.Value);

            if (header.Key.Equals("Location", StringComparison.OrdinalIgnoreCase))
            {
                location = value;
            }
            else
            {
                others[header.Key] = value;
            }
        }

        return new ObservedResponse((int)response.StatusCode, location, others, await response.Content.ReadAsByteArrayAsync());
    }
}

/// <summary>An isolated API host with a controllable coordinator, full-fidelity logging and a status recorder; clients never follow redirects.</summary>
internal sealed class StablePlaybackHost
{
    public StablePlaybackHost(Infrastructure.ApiWebApplicationFactory factory, Action<IServiceCollection>? configure = null)
    {
        WebApplicationFactory<Program> isolated = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPlaybackResolutionCoordinator>();
                services.AddSingleton<IPlaybackResolutionCoordinator>(Coordinator);

                services.AddSingleton(Outcomes);
                services.AddSingleton<IStartupFilter, RequestOutcomeRecorder.Filter>();

                services.AddLogging(logging =>
                {
                    logging.SetMinimumLevel(LogLevel.Trace);

                    // The app's config silences Microsoft.AspNetCore below Warning. Lift that here so a framework redirect helper
                    // (which logs its destination at Information) could not hide from the log scan.
                    logging.AddFilter("Microsoft.AspNetCore", LogLevel.Trace);
                    logging.AddProvider(Logs);
                });

                configure?.Invoke(services);
            }));

        Services = isolated.Services;
        Client = isolated.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public ControllableCoordinator Coordinator { get; } = new();

    public RecordingLoggerProvider Logs { get; } = new();

    public RequestOutcomeRecorder Outcomes { get; } = new();

    public IServiceProvider Services { get; }

    public HttpClient Client { get; }

    public static string RouteFor(Guid movieId) => $"/media/{movieId}/stream";

    public async Task<ObservedResponse> SendAsync(string method, string path, string? range = null, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);

        if (range is not null)
        {
            request.Headers.TryAddWithoutValidation("Range", range);
        }

        using HttpResponseMessage response = await Client.SendAsync(request, cancellationToken);
        return await ObservedResponse.FromAsync(response);
    }
}
