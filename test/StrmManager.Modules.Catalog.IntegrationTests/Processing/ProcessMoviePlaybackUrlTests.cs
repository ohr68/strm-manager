using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Playback;
using StrmManager.Modules.Catalog.Domain.Movies;
using StrmManager.Modules.Catalog.Domain.Shared;
using StrmManager.Modules.Catalog.Domain.StrmFiles;
using StrmManager.Modules.Catalog.Infrastructure.Database;
using StrmManager.Modules.Catalog.IntegrationTests.Infrastructure;
using StrmManager.Modules.MediaProcessing.Application.StrmGeneration;
using StrmManager.Modules.MediaProcessing.Application.Streams;
using static StrmManager.Modules.Catalog.IntegrationTests.Processing.MovieCandidates;
using static StrmManager.Modules.Catalog.IntegrationTests.Processing.ProcessMovieTestSupport;

namespace StrmManager.Modules.Catalog.IntegrationTests.Processing;

/// <summary>
/// ADR-015 for ProcessMovie: a new movie's .strm holds the STABLE playback URL ({PublicBaseUrl}/media/{movieId}/stream), never the
/// provider's ephemeral URL, and a missing/unusable PublicBaseUrl makes ProcessMovie refuse BEFORE it claims or touches anything.
/// PublicBaseUrl reaches the app through the real configuration binding (UseSetting, the path an environment variable takes),
/// so what is proven here is what a deployment would get. An already Completed movie needs no configuration and is never rewritten.
/// </summary>
public class ProcessMoviePlaybackUrlTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private const string OldProviderUrl = "https://old-provider.invalid/OLDTOKEN-77/legacy.m3u8?sig=OLDSIG-19";

    private static readonly string CanaryUrl = "https://canary-host-e52c90.invalid/hls/PATHSECRET-8a13/master.m3u8?sig=QUERYSECRET-d4f7";
    private static readonly string[] CanaryParts = ["canary-host-e52c90", "PATHSECRET-8a13", "QUERYSECRET-d4f7"];

    /// <summary>
    /// A host whose PublicBaseUrl comes through the real configuration binding (UseSetting - the path an environment variable takes).
    /// UseSetting cannot express "absent" (a null binds as an empty string), so for null the bound option is cleared instead: exactly what
    /// an unset key leaves behind.
    /// </summary>
    private ProcessMovieHost HostWith(string? publicBaseUrl, Action<IServiceCollection>? configure = null) =>
        publicBaseUrl is null
            ? new ProcessMovieHost(factory, services =>
            {
                configure?.Invoke(services);
                services.Configure<PlaybackOptions>(options => options.PublicBaseUrl = null);
            })
            : new ProcessMovieHost(factory, configure, builder => builder.UseSetting("Playback:PublicBaseUrl", publicBaseUrl));

    private static Result<IReadOnlyList<StreamCandidate>> Found(params StreamCandidate[] candidates) =>
        Result.Success<IReadOnlyList<StreamCandidate>>(candidates);

    private static object Snapshot(Movie m) =>
        (m.Id, m.ExternalIds, m.Title, m.Year, m.Runtime, m.Status, m.LastAttemptAtUtc, m.NextAttemptAtUtc, m.AttemptCount, m.LastError, m.UpdatedAtUtc);

    private static string Dump(object value) =>
        string.Join('|', value.GetType().GetProperties().Select(p => p.GetValue(value)?.ToString()));

    private static ProcessMovieResponse Parse(string body) => JsonSerializer.Deserialize<ProcessMovieResponse>(body, JsonSerializerOptions.Web)!;

    private static Action<IServiceCollection> RecordWrites(ConcurrentQueue<string> writtenContents) => services =>
    {
        ServiceDescriptor existing = services.Single(d => d.ServiceType == typeof(IStrmWriter));
        services.Remove(existing);
        services.AddSingleton<IStrmWriter>(sp => new RecordingStrmWriter((IStrmWriter)existing.ImplementationFactory!(sp), writtenContents));
    };

    private static void AssertNoCanary(string text)
    {
        foreach (string part in CanaryParts)
        {
            Assert.DoesNotContain(part, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ================= startup vs. ProcessMovie =================

    [Fact]
    public async Task TheApplicationStarts_WithNoPublicBaseUrl_AndKeepsServingEverythingElse()
    {
        var host = HostWith(null);

        Assert.Null(host.Services.GetRequiredService<IOptions<PlaybackOptions>>().Value.PublicBaseUrl); // really unset - not the fixture's value

        HttpResponseMessage response = await host.Client.GetAsync($"/api/movies/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); // the API is up and answering
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/media")]
    [InlineData("relative/path")]
    [InlineData("ftp://strm-manager:8080")]
    [InlineData("not a url")]
    [InlineData("http://user:secret@strm-manager:8080")]
    [InlineData("http://strm-manager:8080/?key=SECRETQUERY")]
    public async Task AMissingOrUnusablePublicBaseUrl_FailsBeforeAnythingIsClaimedOrCalled(string? configured)
    {
        var host = HostWith(configured);
        if (configured is null)
        {
            Assert.Null(host.Services.GetRequiredService<IOptions<PlaybackOptions>>().Value.PublicBaseUrl);
        }

        Guid movieId = await SeedMovieAsync(host.Services, MediaStatus.Pending, year: 2025);
        object before = Snapshot(await LoadMovieAsync(host.Services, movieId));
        host.Provider = _ => Found(Candidate("Source A"));
        host.Validate = _ => Approval;

        (HttpResponseMessage response, string body) = await host.ProcessAsync(movieId);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("Playback.PublicBaseUrlInvalid", JsonSerializer.Deserialize<ProblemResponse>(body, JsonSerializerOptions.Web)!.Title);

        Assert.Empty(host.Events);                    // no claim save, no plain save, no provider, no validator, no writer - nothing at all
        Assert.Empty(host.Validated);
        Assert.Empty(await host.AttemptsAsync(movieId)); // zero SourceAttempts
        Movie after = await LoadMovieAsync(host.Services, movieId);
        Assert.Equal(before, Snapshot(after));        // state, timestamps (incl. the concurrency token), attempt count - all untouched
        Assert.Equal(MediaStatus.Pending, after.Status); // and certainly not stranded in Searching/Validating
        Assert.Empty(Directory.Exists(host.StrmRoot) ? Directory.GetFiles(host.StrmRoot, "*", SearchOption.AllDirectories) : []);

        // The configured value (which may carry a credential) is never echoed or logged.
        foreach (string piece in new[] { "secret", "SECRETQUERY", "strm-manager:8080" })
        {
            Assert.DoesNotContain(piece, body, StringComparison.Ordinal);
            Assert.All(host.Logs.Lines, line => Assert.DoesNotContain(piece, line, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task AnInvalidPublicBaseUrl_MeansNeitherOfTwoConcurrentRequestsCanClaimOrMutateTheMovie()
    {
        var host = HostWith("");
        Guid movieId = await SeedMovieAsync(host.Services, MediaStatus.Pending, year: 2025);
        object before = Snapshot(await LoadMovieAsync(host.Services, movieId));
        host.Provider = _ => Found(Candidate("Source A"));
        host.Validate = _ => Approval;

        (HttpResponseMessage Response, string Body)[] results = await Task.WhenAll(host.ProcessAsync(movieId), host.ProcessAsync(movieId));

        Assert.All(results, r => Assert.Equal(HttpStatusCode.InternalServerError, r.Response.StatusCode)); // both refused for the same reason - no winner
        Assert.Empty(host.Events);
        Assert.Equal(before, Snapshot(await LoadMovieAsync(host.Services, movieId)));
        Assert.Empty(await host.AttemptsAsync(movieId));
    }

    // ================= valid values, normalization =================

    [Theory]
    [InlineData("http://strm-manager:8080", "http://strm-manager:8080")]
    [InlineData("http://192.168.1.10:8101", "http://192.168.1.10:8101")]
    [InlineData("https://media.example.com", "https://media.example.com")]
    [InlineData("https://media.example.com/", "https://media.example.com")]
    [InlineData("https://media.example.com///", "https://media.example.com")]
    [InlineData("http://strm-manager:8080/strm/", "http://strm-manager:8080/strm")]
    public async Task AValidPublicBaseUrl_IsUsedAsConfigured_WithNoDoubleSlash(string configured, string expectedBase)
    {
        var host = HostWith(configured);
        Guid movieId = await SeedMovieAsync(host.Services, MediaStatus.Pending, year: 2025);
        host.Provider = _ => Found(Candidate("Source A"));
        host.Validate = _ => Approval;

        (HttpResponseMessage response, string body) = await host.ProcessAsync(movieId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ProcessMovieResponse result = Parse(body);
        Assert.Equal("Completed", result.Status);

        string content = await File.ReadAllTextAsync(result.StrmPath!);
        Assert.Equal($"{expectedBase}/media/{movieId}/stream", content);
        Assert.DoesNotContain("//media", content[8..], StringComparison.Ordinal);
    }

    // ================= new processing: the stable URL, and nothing else, is written =================

    [Fact]
    public async Task ANewMovie_GetsTheStablePlaybackUrl_InTheWriterCall_AndInTheFile_NeverTheProviderUrl()
    {
        var writes = new ConcurrentQueue<string>();
        var validatedUrls = new List<string>();
        var host = HostWith("https://media.example.com/base/", RecordWrites(writes));
        Guid movieId = await SeedMovieAsync(host.Services, MediaStatus.Pending, year: 2025);
        host.Provider = _ => Found(Candidate("Source A") with { Url = CanaryUrl });
        host.Validate = candidate =>
        {
            validatedUrls.Add(candidate.Url);
            return Approval;
        };

        (HttpResponseMessage response, string body) = await host.ProcessAsync(movieId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ProcessMovieResponse result = Parse(body);
        string expectedUrl = $"https://media.example.com/base/media/{movieId}/stream";

        // Discovery, identity and ffprobe are unchanged: the provider candidate (and ITS url) still goes through validation...
        Assert.Equal([CanaryUrl], validatedUrls);
        Assert.Equal(["Source A"], host.Validated.Select(v => v.CandidateName));
        Assert.Equal(new SelectedSourceResponse("FrostStream", "Source A"), result.SelectedSource);

        // ...but the writer is handed the stable URL, and the file holds only that.
        Assert.Equal([expectedUrl], writes.ToArray());
        string strm = await File.ReadAllTextAsync(result.StrmPath!);
        Assert.Equal(expectedUrl, strm);
        Assert.Matches($"^https://media\\.example\\.com/base/media/{movieId}/stream$", strm); // base + opaque id route, nothing else

        // Persistence semantics as before: Completed, one Approved attempt, the StrmFile at the same path layout.
        Movie movie = await LoadMovieAsync(host.Services, movieId);
        Assert.Equal(MediaStatus.Completed, movie.Status);
        Assert.Equal(1, movie.AttemptCount);
        Assert.Single(await host.AttemptsAsync(movieId));
        Assert.EndsWith(Path.Combine("movies", $"Process Movie Test (2025) [imdbid-{movie.ExternalIds.ImdbId}]", "Process Movie Test (2025).strm"), result.StrmPath);

        // Save/state ordering is exactly what it was, and nothing external happens before the claim.
        Assert.Equal(
            [
                "claim-save:in-memory=Searching", "claim-save:persisted-before=Pending", "claim-save:result=True",
                "external:stream-provider", "plain-save", "external:media-validator", "external:strm-writer", "plain-save",
            ],
            host.Events.ToArray());
    }

    [Fact]
    public async Task TheProviderUrl_AppearsNowhereItCouldBePersistedLoggedOrReturned()
    {
        var host = HostWith("https://media.example.com");
        Guid movieId = await SeedMovieAsync(host.Services, MediaStatus.Pending, year: 2025);
        host.Provider = _ => Found(Candidate("Source A") with { Url = CanaryUrl });
        host.Validate = _ => Approval;

        (_, string body) = await host.ProcessAsync(movieId);
        ProcessMovieResponse result = Parse(body);

        var everywhere = new List<string> { body, await File.ReadAllTextAsync(result.StrmPath!) };
        everywhere.Add(Dump(await LoadMovieAsync(host.Services, movieId)));

        await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
        {
            StrmFile? strmFile = scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Set<StrmFile>().SingleOrDefault(f => f.MovieId == movieId);
            everywhere.Add(Dump(strmFile!));
        }

        everywhere.AddRange((await host.AttemptsAsync(movieId)).Select(a => Dump(a)));
        everywhere.AddRange(host.Logs.Lines);

        Assert.NotEmpty(host.Logs.Lines); // the log capture was really recording
        Assert.All(everywhere, AssertNoCanary);
    }

    [Fact]
    public async Task TheRequestHost_AndForwardedHeaders_NeverInfluenceTheUrl()
    {
        var host = HostWith("https://configured.example.com");
        Guid movieId = await SeedMovieAsync(host.Services, MediaStatus.Pending, year: 2025);
        host.Provider = _ => Found(Candidate("Source A"));
        host.Validate = _ => Approval;

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/movies/{movieId}/process");
        request.Headers.Host = "attacker.example:6666";
        request.Headers.TryAddWithoutValidation("X-Forwarded-Host", "forwarded-evil.example");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "ftp");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Port", "1234");
        request.Headers.TryAddWithoutValidation("Forwarded", "host=forwarded-evil.example;proto=ftp");

        using HttpResponseMessage response = await host.Client.SendAsync(request);
        ProcessMovieResponse result = Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal($"https://configured.example.com/media/{movieId}/stream", await File.ReadAllTextAsync(result.StrmPath!));
    }

    // ================= Completed: idempotent, needs no configuration, never rewritten =================

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://valid.example:8080")]
    public async Task ACompletedMovie_IsReturnedAsIs_WithOrWithoutAPublicBaseUrl_AndItsOldStrmIsNeverRewritten(string? configured)
    {
        var writes = new ConcurrentQueue<string>();
        var host = HostWith(configured, RecordWrites(writes));
        Guid movieId = await SeedMovieAsync(host.Services, MediaStatus.Completed, year: 2025);

        // A movie completed BEFORE this change: its .strm still holds a provider URL. It must stay exactly as it is.
        string oldPath = Path.Combine(host.StrmRoot, "movies", "Old Movie (2025) [imdbid-tt0000001]", "Old Movie (2025).strm");
        Directory.CreateDirectory(Path.GetDirectoryName(oldPath)!);
        await File.WriteAllTextAsync(oldPath, OldProviderUrl);
        await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Set<StrmFile>().Add(StrmFile.ForMovie(movieId, oldPath, SeededAtUtc));
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().SaveChangesAsync();
        }

        object before = Snapshot(await LoadMovieAsync(host.Services, movieId));
        host.Provider = _ => Found(Candidate("Source A"));
        host.Validate = _ => Approval;
        DateTime writtenBefore = File.GetLastWriteTimeUtc(oldPath);

        (HttpResponseMessage response, string body) = await host.ProcessAsync(movieId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ProcessMovieResponse result = Parse(body);
        Assert.Equal("Completed", result.Status);
        Assert.Equal(oldPath, result.StrmPath);
        Assert.Equal("Movie is already Completed - not reprocessed.", result.Reason);

        Assert.Empty(host.Events);       // no provider, no validator, no writer, no save
        Assert.Empty(writes);
        Assert.Equal(OldProviderUrl, await File.ReadAllTextAsync(oldPath)); // untouched - migration is a separate, explicit step
        Assert.Equal(writtenBefore, File.GetLastWriteTimeUtc(oldPath));
        Assert.Equal(before, Snapshot(await LoadMovieAsync(host.Services, movieId)));
        Assert.Empty(await host.AttemptsAsync(movieId));
    }

    [Fact]
    public async Task ASecondProcessCall_OnAMovieJustCompleted_IsIdempotent_AndDoesNotRewriteTheStrm()
    {
        var writes = new ConcurrentQueue<string>();
        var host = HostWith("https://media.example.com", RecordWrites(writes));
        Guid movieId = await SeedMovieAsync(host.Services, MediaStatus.Pending, year: 2025);
        host.Provider = _ => Found(Candidate("Source A"));
        host.Validate = _ => Approval;

        ProcessMovieResponse first = Parse((await host.ProcessAsync(movieId)).Body);
        string contentAfterFirst = await File.ReadAllTextAsync(first.StrmPath!);
        DateTime writtenAt = File.GetLastWriteTimeUtc(first.StrmPath!);
        int eventsAfterFirst = host.Events.Count;

        ProcessMovieResponse second = Parse((await host.ProcessAsync(movieId)).Body);

        Assert.Equal("Completed", second.Status);
        Assert.Equal(first.StrmPath, second.StrmPath);
        Assert.Equal(eventsAfterFirst, host.Events.Count); // the second call did nothing at all
        Assert.Single(writes);                              // one write, ever
        Assert.Equal(contentAfterFirst, await File.ReadAllTextAsync(second.StrmPath!));
        Assert.Equal(writtenAt, File.GetLastWriteTimeUtc(second.StrmPath!));
    }

    private sealed class RecordingStrmWriter(IStrmWriter inner, ConcurrentQueue<string> writtenContents) : IStrmWriter
    {
        public Task<Result<string>> WriteEpisodeAsync(EpisodeStrmReference reference, string sourceUrl, CancellationToken cancellationToken = default) =>
            inner.WriteEpisodeAsync(reference, sourceUrl, cancellationToken);

        public Task<Result<string>> WriteMovieAsync(MovieStrmReference reference, string sourceUrl, CancellationToken cancellationToken = default)
        {
            writtenContents.Enqueue(sourceUrl);
            return inner.WriteMovieAsync(reference, sourceUrl, cancellationToken);
        }
    }
}
