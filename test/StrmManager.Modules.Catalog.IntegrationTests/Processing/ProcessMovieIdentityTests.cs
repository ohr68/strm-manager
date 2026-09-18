using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Domain.Movies;
using StrmManager.Modules.Catalog.Domain.Shared;
using StrmManager.Modules.Catalog.Domain.SourceAttempts;
using StrmManager.Modules.Catalog.Domain.StrmFiles;
using StrmManager.Modules.Catalog.Infrastructure.Database;
using StrmManager.Modules.Catalog.IntegrationTests.Infrastructure;
using StrmManager.Modules.MediaProcessing.Application.Streams;
using static StrmManager.Modules.Catalog.IntegrationTests.Processing.MovieCandidates;
using static StrmManager.Modules.Catalog.IntegrationTests.Processing.ProcessMovieTestSupport;

namespace StrmManager.Modules.Catalog.IntegrationTests.Processing;

/// <summary>
/// Positive-evidence-only movie identity through the whole ProcessMovie flow: a candidate reaches
/// ffprobe only with an explicit matching IMDb id, TMDB id or "(YYYY)" and no contradiction; header-
/// requiring candidates are turned away first; and neither ever spends a media validation on a
/// candidate that fails those checks. Includes the generic regression for the FrostStream collision
/// (one title-keyed candidate list served for two different same-title movies).
/// </summary>
public class ProcessMovieIdentityTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private const string UndeterminedReason = "Could not confirm the candidate's movie identity.";
    private const string ConflictingReason = "Candidate references a different movie.";
    private const string HeadersReason = "Candidate requires custom request headers, which are not supported.";

    private readonly ProcessMovieHost _host = new(factory);

    private void Provide(params StreamCandidate[] candidates) =>
        _host.Provider = _ => Result.Success<IReadOnlyList<StreamCandidate>>(candidates);

    private static ProcessMovieResponse Parse(string body) =>
        JsonSerializer.Deserialize<ProcessMovieResponse>(body, JsonSerializerOptions.Web)!;

    private async Task AssertRejectedBeforeFfprobeAsync(Guid movieId, string expectedReason)
    {
        SourceAttempt attempt = Assert.Single(await _host.AttemptsAsync(movieId));
        Assert.Equal(SourceAttemptResult.Rejected, attempt.Result);
        Assert.Equal(expectedReason, attempt.FailureReason);
        Assert.Equal(movieId, attempt.MovieId);
        Assert.Null(attempt.Duration); // never probed
        Assert.Null(attempt.VideoCodec);
        Assert.Equal(TimeSpan.FromMinutes(100), attempt.ExpectedDuration);

        Assert.Empty(_host.Validated); // ffprobe never called
        Assert.DoesNotContain("external:media-validator", _host.Events);
        Assert.DoesNotContain("external:strm-writer", _host.Events);

        Movie persisted = await LoadMovieAsync(_host.Services, movieId);
        Assert.Equal(MediaStatus.Unavailable, persisted.Status); // the existing "nothing passed" outcome
        Assert.Equal(_host.Time.GetUtcNow().UtcDateTime.AddHours(6), persisted.NextAttemptAtUtc);
    }

    // --- Rejected without ever reaching ffprobe ---

    [Fact]
    public async Task ProcessMovie_UndeterminedCandidate_IsRejectedAndFfprobeIsNeverCalled()
    {
        Guid movieId = await SeedMovieAsync(_host.Services, MediaStatus.Pending, year: 2025);
        Provide(Candidate("Source A", year: null));

        (HttpResponseMessage response, string body) = await _host.ProcessAsync(movieId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ProcessMovieResponse result = Parse(body);
        Assert.Equal("Unavailable", result.Status);
        Assert.Equal(1, result.Attempts); // it was still an evaluated candidate
        Assert.Equal("No candidate passed identity/media validation.", result.Reason);
        await AssertRejectedBeforeFfprobeAsync(movieId, UndeterminedReason);
    }

    [Fact]
    public async Task ProcessMovie_ACandidateWhoseTitleEqualsTheMoviesOwn_IsStillUndetermined()
    {
        Guid movieId = await SeedMovieAsync(_host.Services, MediaStatus.Pending, title: "Exactly This Title", year: 2025);
        Provide(Candidate("Source A", year: null, title: "Exactly This Title"));

        await _host.ProcessAsync(movieId);

        await AssertRejectedBeforeFfprobeAsync(movieId, UndeterminedReason);
    }

    [Fact]
    public async Task ProcessMovie_CandidateNamingADifferentYear_IsConflictingAndRejectedBeforeFfprobe()
    {
        Guid movieId = await SeedMovieAsync(_host.Services, MediaStatus.Pending, year: 2025);
        Provide(Candidate("Source A", year: 2019));

        await _host.ProcessAsync(movieId);

        await AssertRejectedBeforeFfprobeAsync(movieId, ConflictingReason);
    }

    [Fact]
    public async Task ProcessMovie_CandidateNamingADifferentImdbId_IsConflictingAndRejectedBeforeFfprobe()
    {
        Guid movieId = await SeedMovieAsync(_host.Services, MediaStatus.Pending, year: 2025);
        Provide(Candidate("Source A", year: 2025, extraDescription: "tt9999999")); // matching year, but another film's id

        await _host.ProcessAsync(movieId);

        await AssertRejectedBeforeFfprobeAsync(movieId, ConflictingReason);
    }

    [Fact]
    public async Task ProcessMovie_CandidateWithADifferentTmdbId_IsConflictingAndRejectedBeforeFfprobe()
    {
        Guid movieId = await SeedMovieAsync(_host.Services, MediaStatus.Pending, year: 2025, tmdbId: "8587");
        Provide(Candidate("Source A", year: 2025, tmdbId: "420818"));

        await _host.ProcessAsync(movieId);

        await AssertRejectedBeforeFfprobeAsync(movieId, ConflictingReason);
    }

    [Fact]
    public async Task ProcessMovie_MatchingTmdbIdDoesNotOverrideAConflictingYear()
    {
        Guid movieId = await SeedMovieAsync(_host.Services, MediaStatus.Pending, year: 2025, tmdbId: "8587");
        Provide(Candidate("Source A", year: 2019, tmdbId: "8587")); // contradiction wins over positive evidence

        await _host.ProcessAsync(movieId);

        await AssertRejectedBeforeFfprobeAsync(movieId, ConflictingReason);
    }

    [Fact]
    public async Task ProcessMovie_CandidateTmdbId_WhenTheMovieHasNoTmdbId_IsNotEvidence()
    {
        // Movies added before Cinemeta's moviedb_id was mapped have a null TmdbId.
        Guid movieId = await SeedMovieAsync(_host.Services, MediaStatus.Pending, year: 2025, tmdbId: null);
        Provide(Candidate("Source A", year: null, tmdbId: "8587"));

        await _host.ProcessAsync(movieId);

        await AssertRejectedBeforeFfprobeAsync(movieId, UndeterminedReason);
    }

    // --- Reaching ffprobe (and completing) ---

    [Fact]
    public async Task ProcessMovie_CandidateWithTheMoviesExplicitYear_IsCompatibleAndReachesFfprobe()
    {
        Guid movieId = await SeedMovieAsync(_host.Services, MediaStatus.Pending, year: 2025);
        Provide(Candidate("Source A", year: 2025));
        _host.Validate = _ => Approval;

        (_, string body) = await _host.ProcessAsync(movieId);

        Assert.Equal(["Source A"], _host.Validated.Select(v => v.CandidateName).ToArray());
        Assert.Equal("Completed", Parse(body).Status);
    }

    [Fact]
    public async Task ProcessMovie_CandidateWithTheMoviesImdbId_IsConfirmedAndReachesFfprobe()
    {
        string imdbId = NextImdbId();
        Guid movieId = await SeedMovieAsync(_host.Services, MediaStatus.Pending, year: 2025, imdbId: imdbId);
        Provide(Candidate("Source A", year: null, extraDescription: imdbId));
        _host.Validate = _ => Approval;

        (_, string body) = await _host.ProcessAsync(movieId);

        Assert.Equal(["Source A"], _host.Validated.Select(v => v.CandidateName).ToArray());
        Assert.Equal("Completed", Parse(body).Status);
    }

    [Fact]
    public async Task ProcessMovie_CandidateWithTheMoviesTmdbId_IsConfirmedAndReachesFfprobe()
    {
        Guid movieId = await SeedMovieAsync(_host.Services, MediaStatus.Pending, year: 2025, tmdbId: "8587");
        Provide(Candidate("Source A", year: null, tmdbId: "8587"));
        _host.Validate = _ => Approval;

        (_, string body) = await _host.ProcessAsync(movieId);

        Assert.Equal(["Source A"], _host.Validated.Select(v => v.CandidateName).ToArray());
        Assert.Equal("Completed", Parse(body).Status);
    }

    [Fact]
    public async Task ProcessMovie_MixedCandidates_RejectsTheUnprovenOnesWithASourceAttemptEachAndProbesOnlyTheFirstProvable()
    {
        Guid movieId = await SeedMovieAsync(_host.Services, MediaStatus.Pending, year: 2025);
        Provide(
            Candidate("Source A", year: null), // undetermined
            Candidate("Source B", year: 2019), // conflicting
            Candidate("Source C", year: 2025), // compatible
            Candidate("Source D", year: 2025)); // never reached
        _host.Validate = _ => Approval;

        (_, string body) = await _host.ProcessAsync(movieId);

        ProcessMovieResponse result = Parse(body);
        Assert.Equal("Completed", result.Status);
        Assert.Equal(3, result.Attempts);
        Assert.Equal(["Source C"], _host.Validated.Select(v => v.CandidateName).ToArray());

        // Compared by candidate, not by position: the two pre-ffprobe rejections are recorded within
        // the same fake-clock instant, and SourceAttempts are listed by AttemptedAtUtc alone.
        Dictionary<string, SourceAttempt> attempts = (await _host.AttemptsAsync(movieId)).ToDictionary(a => a.SourceName);
        Assert.Equal(["Source A", "Source B", "Source C"], attempts.Keys.Order().ToArray());
        Assert.Equal(SourceAttemptResult.Rejected, attempts["Source A"].Result);
        Assert.Equal(UndeterminedReason, attempts["Source A"].FailureReason);
        Assert.Equal(SourceAttemptResult.Rejected, attempts["Source B"].Result);
        Assert.Equal(ConflictingReason, attempts["Source B"].FailureReason);
        Assert.Equal(SourceAttemptResult.Approved, attempts["Source C"].Result);
        Assert.Null(attempts["Source C"].FailureReason);
    }

    // --- The Lion King collision class, generically (no title logic anywhere) ---

    [Fact]
    public async Task ProcessMovie_SameTitleMovies_ServedTheSameTitleOnlyCandidates_NeitherIsLinked()
    {
        // Two different movies, one title, different years and ids - and the provider serves the
        // identical title-only candidates (no year, no IMDb id, no TMDB id) for both, as FrostStream did.
        const string sharedTitle = "Some Shared Title";
        Guid earlier = await SeedMovieAsync(_host.Services, MediaStatus.Pending, title: sharedTitle, year: 1994, tmdbId: "8587");
        Guid later = await SeedMovieAsync(_host.Services, MediaStatus.Pending, title: sharedTitle, year: 2019, tmdbId: "420818");
        Provide(
            Candidate("FrostStream 4K", year: null, title: "SOME SHARED TITLE LOCALIZED"),
            Candidate("FrostStream", year: null, title: "SOME SHARED TITLE LOCALIZED"));

        await _host.ProcessAsync(earlier);
        await _host.ProcessAsync(later);

        Assert.Empty(_host.Validated); // ffprobe never ran for either movie
        Assert.DoesNotContain("external:strm-writer", _host.Events);

        foreach (Guid movieId in new[] { earlier, later })
        {
            Movie movie = await LoadMovieAsync(_host.Services, movieId);
            Assert.Equal(MediaStatus.Unavailable, movie.Status);

            IReadOnlyList<SourceAttempt> attempts = await _host.AttemptsAsync(movieId);
            Assert.Equal(2, attempts.Count);
            Assert.All(attempts, a =>
            {
                Assert.Equal(SourceAttemptResult.Rejected, a.Result);
                Assert.Equal(UndeterminedReason, a.FailureReason);
            });

            await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
            Assert.False(await scope.ServiceProvider.GetRequiredService<CatalogDbContext>()
                .Set<StrmFile>().AnyAsync(f => f.MovieId == movieId)); // no StrmFile, so no STRM
        }

        Assert.False(Directory.Exists(Path.Combine(_host.StrmRoot, "movies")) &&
                     Directory.EnumerateFileSystemEntries(Path.Combine(_host.StrmRoot, "movies"), "*Some Shared Title*").Any());
    }

    [Fact]
    public async Task ProcessMovie_SameTitleMovies_ACandidateNamingItsYear_ProceedsOnlyForThatMovie()
    {
        const string sharedTitle = "Another Shared Title";
        Guid earlier = await SeedMovieAsync(_host.Services, MediaStatus.Pending, title: sharedTitle, year: 1994);
        Guid later = await SeedMovieAsync(_host.Services, MediaStatus.Pending, title: sharedTitle, year: 2019);
        Provide(Candidate("FrostStream", year: 2019, title: "OUTRO TITULO"));
        _host.Validate = _ => Approval;

        (_, string earlierBody) = await _host.ProcessAsync(earlier);
        (_, string laterBody) = await _host.ProcessAsync(later);

        Assert.Equal("Unavailable", Parse(earlierBody).Status); // the year contradicts the 1994 movie
        Assert.Equal("Completed", Parse(laterBody).Status); // and matches the 2019 one
        Assert.Equal(new string?[] { ConflictingReason }, (await _host.AttemptsAsync(earlier)).Select(a => a.FailureReason).ToArray());
        Assert.Single(_host.Validated); // ffprobe ran once - for the 2019 movie only
    }

    // --- Unsupported: custom request headers ---

    [Fact]
    public async Task ProcessMovie_HeaderRequiringCandidate_IsRejectedBeforeIdentityAndFfprobe_EvenWhenItsIdentityIsConfirmed()
    {
        string imdbId = NextImdbId();
        Guid movieId = await SeedMovieAsync(_host.Services, MediaStatus.Pending, year: 2025, imdbId: imdbId);
        // Would be Confirmed (the movie's own IMDb id AND year) - but it needs headers we cannot send.
        Provide(Candidate("Source A", year: 2025, extraDescription: imdbId, requiresCustomHeaders: true));

        (_, string body) = await _host.ProcessAsync(movieId);

        Assert.Equal("Unavailable", Parse(body).Status);
        await AssertRejectedBeforeFfprobeAsync(movieId, HeadersReason);
    }

    [Fact]
    public async Task ProcessMovie_HeaderRequiringCandidateFirst_IsSkippedAndTheNextSupportedCandidateIsUsed()
    {
        Guid movieId = await SeedMovieAsync(_host.Services, MediaStatus.Pending, year: 2025);
        Provide(
            Candidate("Needs Headers", year: 2025, requiresCustomHeaders: true),
            Candidate("Plain Source", year: 2025));
        _host.Validate = _ => Approval;

        (_, string body) = await _host.ProcessAsync(movieId);

        ProcessMovieResponse result = Parse(body);
        Assert.Equal("Completed", result.Status);
        Assert.Equal(2, result.Attempts);
        Assert.Equal(new SelectedSourceResponse("FrostStream", "Plain Source"), result.SelectedSource);
        Assert.Equal(["Plain Source"], _host.Validated.Select(v => v.CandidateName).ToArray());

        IReadOnlyList<SourceAttempt> attempts = await _host.AttemptsAsync(movieId);
        Assert.Equal(new string?[] { HeadersReason, null }, attempts.Select(a => a.FailureReason).ToArray());
    }

    [Fact]
    public async Task ProcessMovie_ThroughTheRealFrostStreamProvider_HeaderValuesAndUrlsNeverReachAnythingTheAppPersistsReturnsOrLogs()
    {
        const string wire = """
            {"streams":[
              {"name":"FrostStream 720p","title":"🎬 Titulo (2025)\n🍃 MegaEmbed\n🌎 Português",
               "url":"https://media.example.test/mega?token=SECRET-TOKEN-MEGA",
               "headers":{"User-Agent":"HEADER-SECRET-UA","Referer":"https://referer.example.test/HEADER-SECRET-REF","Origin":"https://origin.example.test"},
               "behaviorHints":{"notWebReady":true,"bingeGroup":"megaembed-movie-8587"}},
              {"name":"FrostStream 1080p","title":"🎬 Titulo (2025)\n🌊 Space\n🌎 Português",
               "url":"https://media.example.test/space?token=SECRET-TOKEN-SPACE",
               "behaviorHints":{"notWebReady":true,"bingeGroup":"cloutstream-cs:movie:titulo-2025"}}
            ]}
            """;
        var handler = new StubFrostStreamHandler(wire);
        var host = new ProcessMovieHost(factory, services =>
        {
            // Put the real FrostStream typed client back, with only its HTTP faked. Both the host and the
            // base factory registered a FakeStreamProvider, so every fake registration has to go.
            foreach (ServiceDescriptor fake in services
                .Where(d => d.ServiceType == typeof(IStreamProvider) &&
                            (d.ImplementationType == typeof(FakeStreamProvider) || d.ImplementationInstance is FakeStreamProvider))
                .ToList())
            {
                services.Remove(fake);
            }

            services.ConfigureAll<HttpClientFactoryOptions>(options =>
                options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = handler));
        });
        host.Validate = _ => Approval;
        Guid movieId = await SeedMovieAsync(host.Services, MediaStatus.Pending, year: 2025);

        (HttpResponseMessage response, string body) = await host.ProcessAsync(movieId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ProcessMovieResponse result = Parse(body);
        Assert.Equal("Completed", result.Status);
        Assert.Equal(new SelectedSourceResponse("FrostStream", "FrostStream 1080p"), result.SelectedSource);
        Assert.Equal(2, result.Attempts);
        Assert.Equal(["FrostStream 1080p"], host.Validated.Select(v => v.CandidateName).ToArray()); // the header candidate was never probed

        IReadOnlyList<SourceAttempt> attempts = await host.AttemptsAsync(movieId);
        Assert.Equal(new string?[] { HeadersReason, null }, attempts.Select(a => a.FailureReason).ToArray());

        // The URL lives in exactly one place - the .strm content. Everything else is free of both URLs and header values.
        string strmContent = await File.ReadAllTextAsync(result.StrmPath!);
        Assert.Equal("https://media.example.test/space?token=SECRET-TOKEN-SPACE", strmContent);

        Movie movie = await LoadMovieAsync(host.Services, movieId);
        var everythingElse = new List<string> { body, movie.LastError ?? string.Empty, result.Reason ?? string.Empty };
        everythingElse.AddRange(attempts.Select(a => $"{a.Provider} {a.SourceName} {a.FailureReason} {a.VideoCodec} {a.AudioCodec}"));
        everythingElse.AddRange(host.Logs.Lines);

        Assert.All(everythingElse, text =>
        {
            Assert.DoesNotContain("SECRET-TOKEN", text, StringComparison.Ordinal);
            Assert.DoesNotContain("media.example.test", text, StringComparison.Ordinal);
            Assert.DoesNotContain("HEADER-SECRET", text, StringComparison.Ordinal);
            Assert.DoesNotContain("referer.example.test", text, StringComparison.Ordinal);
        });
        Assert.NotEmpty(host.Logs.Lines); // the capture really was recording
    }

    private sealed class StubFrostStreamHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }
}
