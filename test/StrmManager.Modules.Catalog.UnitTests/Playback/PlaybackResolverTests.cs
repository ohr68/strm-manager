using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Playback;
using StrmManager.Modules.Catalog.Domain.Movies;
using StrmManager.Modules.Catalog.Domain.Shared;
using StrmManager.Modules.Catalog.Domain.SourceAttempts;
using StrmManager.Modules.MediaProcessing.Application.Streams;
using StrmManager.Modules.MediaProcessing.Application.Validation;

namespace StrmManager.Modules.Catalog.UnitTests.Playback;

/// <summary>
/// PlaybackResolver in isolation: in-memory fakes, no database, no HTTP. The candidate policy itself
/// (headers, identity, ffprobe order) is MovieSourceSelector's and is characterized there; here it is only shown
/// to be USED - and that the resolver is read-only and never lets a provider URL out except in the one place
/// meant for it.
/// </summary>
public class PlaybackResolverTests
{
    private const string Imdb = "tt0111161";
    private const string Tmdb = "278";
    private const int Year = 1994;
    private static readonly TimeSpan Runtime = TimeSpan.FromMinutes(142);

    // Unique, secret-looking parts of every candidate URL, plus the raw texts a provider/validator could produce.
    private const string CanaryHost = "canary-host-6c1e83.invalid";
    private const string PathSecret = "PATHSECRET-7a90";
    private const string QuerySecret = "QUERYSECRET-31bd";
    private const string RawProviderText = "RAWPROVIDERTEXT-5502";
    private const string RawValidatorText = "RAWVALIDATORTEXT-8814";

    private static readonly string[] AllSecrets = [CanaryHost, PathSecret, QuerySecret, RawProviderText, RawValidatorText];

    private static readonly DateTime Seeded = new(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);

    // ---- Fixtures ----

    private static string UrlFor(string label) => $"https://{CanaryHost}/hls/{PathSecret}/{label}.m3u8?sig={QuerySecret}-{label}";

    private static StreamCandidate Compatible(string label) => new("FrostStream", $"{label} ({Year})", null, UrlFor(label));

    private static StreamCandidate Conflicting(string label) => new("FrostStream", $"{label} (2019)", null, UrlFor(label));

    private static StreamCandidate Undetermined(string label) => new("FrostStream", label, null, UrlFor(label));

    private static MediaValidationResult Approved() => MediaValidationResult.ForApproval(Runtime, Runtime, 0, "h264", "aac");

    private static MediaValidationResult Bad() =>
        MediaValidationResult.ForRejection(SourceAttemptResult.InvalidMedia, $"{RawValidatorText} {UrlFor("echoed")}", TimeSpan.FromMinutes(5), Runtime, 96.5, "mpeg4", null);

    private static Movie MovieIn(MediaStatus status, string? imdb = Imdb)
    {
        DateTime release = status == MediaStatus.Scheduled ? Seeded.AddDays(30) : Seeded.AddDays(-30);
        var movie = Movie.Schedule(new ExternalIds(imdb, Tmdb, null), "The Shawshank Redemption", Year, Runtime, release, Seeded);

        switch (status)
        {
            case MediaStatus.Scheduled:
            case MediaStatus.Pending:
                break;
            case MediaStatus.Searching:
                movie.StartSearching(Seeded);
                break;
            case MediaStatus.Validating:
                movie.StartSearching(Seeded);
                movie.StartValidating(Seeded);
                break;
            case MediaStatus.Completed:
                movie.StartSearching(Seeded);
                movie.MarkCompleted(Seeded);
                break;
            case MediaStatus.Unavailable:
                movie.StartSearching(Seeded);
                movie.MarkUnavailable(Seeded, Seeded.AddHours(6), "no candidate passed validation");
                break;
            case MediaStatus.Error:
                movie.StartSearching(Seeded);
                movie.MarkError(Seeded, "stream provider unavailable", Seeded.AddMinutes(15));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }

        Assert.Equal(status, movie.Status);
        return movie;
    }

    private static object Snapshot(Movie m) =>
        (m.Id, m.ExternalIds, m.Title, m.Year, m.Runtime, m.ReleaseAtUtc, m.Status, m.LastAttemptAtUtc, m.NextAttemptAtUtc, m.AttemptCount, m.LastError, m.CreatedAtUtc, m.UpdatedAtUtc);

    private sealed class Harness
    {
        public Harness(Movie? movie)
        {
            Movie = movie;
            Repository = new FakeMovieRepository(movie);
            Resolver = new PlaybackResolver(Repository, Provider, Validator, Logger);
        }

        public Movie? Movie { get; }

        public FakeMovieRepository Repository { get; }

        public FakeStreamProvider Provider { get; } = new();

        public RecordingValidator Validator { get; } = new();

        public CapturingLogger<PlaybackResolver> Logger { get; } = new();

        public PlaybackResolver Resolver { get; }

        public Task<PlaybackResolutionResult> ResolveAsync(CancellationToken ct = default) =>
            Resolver.ResolveAsync(Movie?.Id ?? Guid.NewGuid(), ct);

        /// <summary>Every place a secret could be observed: the result's text, its JSON, and every logged message/property/exception.</summary>
        public string EverythingObservable(PlaybackResolutionResult result) =>
            string.Join('\n', result.ToString(), JsonSerializer.Serialize(result, result.GetType()), Logger.AllText);
    }

    private static void AssertNoSecrets(string text)
    {
        foreach (string secret in AllSecrets)
        {
            Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---- 1-7: NotFound, and the provider is never asked ----

    [Fact]
    public async Task UnknownMovie_IsNotFound_AndTheProviderIsNotCalled()
    {
        var h = new Harness(movie: null);
        h.Provider.Candidates = [Compatible("a")];

        PlaybackResolutionResult result = await h.ResolveAsync();

        Assert.IsType<PlaybackResolutionResult.NotFound>(result);
        Assert.Equal(0, h.Provider.Calls);
        Assert.Empty(h.Validator.Calls);
    }

    [Theory]
    [InlineData(MediaStatus.Pending)]
    [InlineData(MediaStatus.Scheduled)]
    [InlineData(MediaStatus.Searching)]
    [InlineData(MediaStatus.Validating)]
    [InlineData(MediaStatus.Unavailable)]
    [InlineData(MediaStatus.Error)]
    public async Task MovieThatIsNotCompleted_IsNotFound_AndTheProviderIsNotCalled(MediaStatus status)
    {
        var h = new Harness(MovieIn(status));
        h.Provider.Candidates = [Compatible("a")];

        PlaybackResolutionResult result = await h.ResolveAsync();

        Assert.IsType<PlaybackResolutionResult.NotFound>(result);
        Assert.Equal(0, h.Provider.Calls);
        Assert.Empty(h.Validator.Calls);
    }

    [Fact]
    public async Task NotFound_DoesNotRevealWhetherTheMovieIsMissingOrJustNotCompleted()
    {
        var missing = new Harness(movie: null);
        var pending = new Harness(MovieIn(MediaStatus.Pending));

        Assert.Equal(await missing.ResolveAsync(), await pending.ResolveAsync()); // one indistinguishable value
    }

    // ---- 8-11: Completed -> provider consulted -> Resolved / Unavailable ----

    [Fact]
    public async Task CompletedMovie_AsksTheProviderForFreshCandidates()
    {
        var h = new Harness(MovieIn(MediaStatus.Completed));
        h.Provider.Candidates = [Compatible("a")];
        h.Validator.Verdict = _ => Approved();

        await h.ResolveAsync();

        Assert.Equal(1, h.Provider.Calls);
    }

    [Fact]
    public async Task ProviderFailure_IsUnavailable_WithNoProviderTextInTheResult()
    {
        var h = new Harness(MovieIn(MediaStatus.Completed));
        h.Provider.Failure = Error.Failure("Streams.InvalidResponse", $"{RawProviderText} {UrlFor("from-provider")}");

        PlaybackResolutionResult result = await h.ResolveAsync();

        Assert.Equal(new PlaybackResolutionResult.Unavailable(PlaybackUnavailableReason.ProviderFailure), result);
        Assert.Empty(h.Validator.Calls);
        Assert.Contains("Streams.InvalidResponse", h.Logger.AllText); // the stable code is kept for diagnosis
        AssertNoSecrets(h.EverythingObservable(result)); // the raw description is not
    }

    [Fact]
    public async Task ZeroCandidates_IsUnavailable()
    {
        var h = new Harness(MovieIn(MediaStatus.Completed));
        h.Provider.Candidates = [];

        PlaybackResolutionResult result = await h.ResolveAsync();

        Assert.Equal(new PlaybackResolutionResult.Unavailable(PlaybackUnavailableReason.NoCandidates), result);
        Assert.Empty(h.Validator.Calls);
    }

    [Fact]
    public async Task CompletedMovieWithoutAnImdbId_IsUnavailable_WithoutCallingTheProvider()
    {
        var h = new Harness(MovieIn(MediaStatus.Completed, imdb: null));
        h.Provider.Candidates = [Compatible("a")];

        PlaybackResolutionResult result = await h.ResolveAsync();

        Assert.Equal(new PlaybackResolutionResult.Unavailable(PlaybackUnavailableReason.MissingImdbId), result);
        Assert.Equal(0, h.Provider.Calls);
    }

    [Fact]
    public async Task FirstApprovedCandidate_IsResolved_AndCarriesExactlyItsUrl()
    {
        var h = new Harness(MovieIn(MediaStatus.Completed));
        StreamCandidate winner = Compatible("winner");
        h.Provider.Candidates = [Compatible("bad-1"), winner, Compatible("later")];
        h.Validator.Verdict = name => name.StartsWith("bad", StringComparison.Ordinal) ? Bad() : Approved();

        PlaybackResolutionResult result = await h.ResolveAsync();

        var resolved = Assert.IsType<PlaybackResolutionResult.Resolved>(result);
        Assert.Equal(winner.Url, resolved.Location.Reveal()); // the internal carrier holds exactly the approved URL
        Assert.Equal("FrostStream", resolved.Provider);
        Assert.Equal(winner.Name, resolved.SourceName);
        Assert.Equal(2, h.Validator.Calls.Count); // "later" was never validated
    }

    [Fact]
    public async Task RejectedCandidate_DoesNotStopTheSearch()
    {
        var h = new Harness(MovieIn(MediaStatus.Completed));
        h.Provider.Candidates = [Compatible("bad-1"), Compatible("bad-2"), Compatible("good-3")];
        h.Validator.Verdict = name => name.StartsWith("bad", StringComparison.Ordinal) ? Bad() : Approved();

        PlaybackResolutionResult result = await h.ResolveAsync();

        Assert.Equal(UrlFor("good-3"), Assert.IsType<PlaybackResolutionResult.Resolved>(result).Location.Reveal());
        Assert.Equal(3, h.Validator.Calls.Count);
    }

    // ---- 13-16: the selector's policy is what applies ----

    [Fact]
    public async Task CustomHeaderCandidate_NeverReachesTheMediaValidator()
    {
        var h = new Harness(MovieIn(MediaStatus.Completed));
        h.Provider.Candidates = [Compatible("headers") with { RequiresCustomHeaders = true }, Compatible("good")];
        h.Validator.Verdict = _ => Approved();

        PlaybackResolutionResult result = await h.ResolveAsync();

        Assert.Equal(["good (1994)"], h.Validator.Calls);
        Assert.Equal(UrlFor("good"), Assert.IsType<PlaybackResolutionResult.Resolved>(result).Location.Reveal());
    }

    [Fact]
    public async Task ConflictingIdentity_NeverReachesTheMediaValidator()
    {
        var h = new Harness(MovieIn(MediaStatus.Completed));
        h.Provider.Candidates = [Conflicting("other-movie")];
        h.Validator.Verdict = _ => Approved();

        PlaybackResolutionResult result = await h.ResolveAsync();

        Assert.Empty(h.Validator.Calls);
        Assert.Equal(new PlaybackResolutionResult.Unavailable(PlaybackUnavailableReason.NoApprovedCandidate), result);
    }

    [Fact]
    public async Task UndeterminedIdentity_NeverReachesTheMediaValidator()
    {
        var h = new Harness(MovieIn(MediaStatus.Completed));
        h.Provider.Candidates = [Undetermined("no-evidence")];
        h.Validator.Verdict = _ => Approved();

        PlaybackResolutionResult result = await h.ResolveAsync();

        Assert.Empty(h.Validator.Calls);
        Assert.Equal(new PlaybackResolutionResult.Unavailable(PlaybackUnavailableReason.NoApprovedCandidate), result);
    }

    [Fact]
    public async Task NoApprovedCandidate_IsUnavailable_WithNoValidatorTextInTheResultOrLogs()
    {
        var h = new Harness(MovieIn(MediaStatus.Completed));
        h.Provider.Candidates = [Compatible("bad-1"), Compatible("bad-2")];
        h.Validator.Verdict = _ => Bad();

        PlaybackResolutionResult result = await h.ResolveAsync();

        Assert.Equal(new PlaybackResolutionResult.Unavailable(PlaybackUnavailableReason.NoApprovedCandidate), result);
        AssertNoSecrets(h.EverythingObservable(result));
    }

    [Fact]
    public async Task CandidateOrder_IsPreserved()
    {
        var h = new Harness(MovieIn(MediaStatus.Completed));
        h.Provider.Candidates = [Compatible("c1"), Undetermined("c2"), Compatible("c3"), Compatible("c4")];
        h.Validator.Verdict = _ => Bad();

        await h.ResolveAsync();

        Assert.Equal(["c1 (1994)", "c3 (1994)", "c4 (1994)"], h.Validator.Calls);
    }

    // ---- The references handed to the provider / identity / validator come from the Movie ----

    [Fact]
    public async Task ProviderIsGivenTheSameMovieStreamReferenceProcessMovieBuilds()
    {
        Movie movie = MovieIn(MediaStatus.Completed);
        var h = new Harness(movie);
        h.Provider.Candidates = [];

        await h.ResolveAsync();

        // ProcessMovieCommandHandler: new MovieStreamReference(ExternalIds.ImdbId, Title, Year, Runtime)
        Assert.Equal(new MovieStreamReference(Imdb, "The Shawshank Redemption", Year, Runtime), Assert.Single(h.Provider.References));
    }

    [Fact]
    public async Task ValidatorIsGivenTheMoviesRuntime_AndIdentityUsesItsTmdbIdAndYear()
    {
        var h = new Harness(MovieIn(MediaStatus.Completed));
        h.Provider.Candidates =
        [
            new("FrostStream", "tmdb-only", null, UrlFor("tmdb-ok"), TmdbId: Tmdb),   // Confirmed by the structured TMDB id alone
            new("FrostStream", "wrong-tmdb", null, UrlFor("tmdb-bad"), TmdbId: "999"), // Conflicting -> never validated
        ];
        h.Validator.Verdict = _ => Bad();

        await h.ResolveAsync();

        Assert.Equal(["tmdb-only"], h.Validator.Calls);
        Assert.Equal([new MediaValidationReference(Runtime)], h.Validator.References);
    }

    // ---- 18: cancellation ----

    [Fact]
    public async Task CancelledBeforeStarting_Propagates_WithoutValidating()
    {
        var h = new Harness(MovieIn(MediaStatus.Completed));
        h.Provider.Candidates = [Compatible("a")];
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => h.ResolveAsync(cts.Token));

        Assert.Empty(h.Validator.Calls);
    }

    [Fact]
    public async Task CancelledDuringValidation_Propagates_AndTheTokenReachesEveryCollaborator()
    {
        var h = new Harness(MovieIn(MediaStatus.Completed));
        h.Provider.Candidates = [Compatible("a"), Compatible("b")];
        using var cts = new CancellationTokenSource();
        h.Validator.Verdict = _ =>
        {
            cts.Cancel();
            return Bad();
        };

        await Assert.ThrowsAsync<OperationCanceledException>(() => h.ResolveAsync(cts.Token));

        Assert.Single(h.Validator.Calls); // "b" was never validated
        Assert.Equal([cts.Token], h.Repository.Tokens);
        Assert.Equal([cts.Token], h.Provider.Tokens);
        Assert.Equal([cts.Token], h.Validator.Tokens);
    }

    // ---- 19-22: read-only ----

    [Theory]
    [InlineData("resolved")]
    [InlineData("no-candidates")]
    [InlineData("none-approved")]
    [InlineData("provider-failure")]
    public async Task Resolution_NeverChangesTheMovie_WhateverTheOutcome(string outcome)
    {
        Movie movie = MovieIn(MediaStatus.Completed);
        object before = Snapshot(movie);
        var h = new Harness(movie);

        switch (outcome)
        {
            case "resolved": h.Provider.Candidates = [Compatible("a")]; h.Validator.Verdict = _ => Approved(); break;
            case "no-candidates": h.Provider.Candidates = []; break;
            case "none-approved": h.Provider.Candidates = [Compatible("a")]; h.Validator.Verdict = _ => Bad(); break;
            default: h.Provider.Failure = StreamProviderErrors.Timeout("FrostStream"); break;
        }

        await h.ResolveAsync();

        Assert.Equal(before, Snapshot(movie)); // status, timestamps (incl. the concurrency token), attempt count, last error - all untouched
        Assert.Equal(0, h.Repository.Inserts);
    }

    [Fact]
    public void Resolver_HasNoDependencyThatCouldSaveRecordAttemptsOrWriteAStrm()
    {
        // Read-only by construction: the only things it is given are a movie reader, the provider, the validator and a logger.
        Type[] parameters = typeof(PlaybackResolver).GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ToArray();

        Assert.Equal(
            [typeof(IMovieRepository), typeof(IStreamProvider), typeof(IMediaValidator), typeof(ILogger<PlaybackResolver>)],
            parameters);
    }

    // ---- 23-26: URL confidentiality ----

    [Fact]
    public async Task ResolvedResult_NeverPrintsTheUrl_ByToString_Formatting_Json_OrLogging()
    {
        var h = new Harness(MovieIn(MediaStatus.Completed));
        StreamCandidate winner = Compatible("winner");
        h.Provider.Candidates = [winner];
        h.Validator.Verdict = _ => Approved();

        PlaybackResolutionResult result = await h.ResolveAsync();
        var resolved = Assert.IsType<PlaybackResolutionResult.Resolved>(result);

        Assert.Equal(winner.Url, resolved.Location.Reveal()); // the ONLY place the URL is readable...
        Assert.Contains("[redacted]", resolved.ToString());

        // ...and every other way of observing the result is free of it.
        string observable = string.Join('\n',
            resolved.ToString(),
            resolved.Location.ToString(),
            $"{resolved}",
            $"{resolved.Location}",
            string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} {1}", resolved, resolved.Location),
            JsonSerializer.Serialize(resolved),
            JsonSerializer.Serialize(resolved.Location),
            h.Logger.AllText);

        AssertNoSecrets(observable);
    }

    [Fact]
    public async Task ResolvingLogsSomething_ButNeverAUrl_InAnyScenario()
    {
        foreach (string scenario in new[] { "resolved", "none-approved", "no-candidates", "provider-failure", "headers", "conflict", "undetermined" })
        {
            var h = new Harness(MovieIn(MediaStatus.Completed));

            switch (scenario)
            {
                case "resolved": h.Provider.Candidates = [Compatible("a")]; h.Validator.Verdict = _ => Approved(); break;
                case "none-approved": h.Provider.Candidates = [Compatible("a")]; h.Validator.Verdict = _ => Bad(); break;
                case "no-candidates": h.Provider.Candidates = []; break;
                case "provider-failure": h.Provider.Failure = Error.Failure("Streams.Timeout", $"{RawProviderText} {UrlFor("t")}"); break;
                case "headers": h.Provider.Candidates = [Compatible("a") with { RequiresCustomHeaders = true }]; break;
                case "conflict": h.Provider.Candidates = [Conflicting("a")]; break;
                default: h.Provider.Candidates = [Undetermined("a")]; break;
            }

            PlaybackResolutionResult result = await h.ResolveAsync();

            Assert.NotEmpty(h.Logger.Entries);
            AssertNoSecrets(h.Logger.AllText);

            if (result is not PlaybackResolutionResult.Resolved)
            {
                AssertNoSecrets(h.EverythingObservable(result)); // failures carry no URL at all, not even in the intended carrier
            }
        }
    }

    [Fact]
    public void PlaybackLocation_RejectsAnEmptyUrl()
    {
        Assert.Throws<ArgumentException>(() => new PlaybackLocation(" "));
    }

    // ---- Fakes ----

    private sealed class FakeMovieRepository(Movie? movie) : IMovieRepository
    {
        public List<CancellationToken> Tokens { get; } = [];

        public int Inserts { get; private set; }

        public Task<Movie?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Tokens.Add(cancellationToken);
            return Task.FromResult(movie is not null && movie.Id == id ? movie : null);
        }

        public void Insert(Movie movie) => Inserts++;

        public Task<Movie?> GetByImdbIdAsync(string imdbId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<Movie>> GetScheduledDueAsync(DateTime utcNow, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<Movie>> GetRetryableAsync(DateTime utcNow, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeStreamProvider : IStreamProvider
    {
        public int Calls { get; private set; }

        public List<MovieStreamReference> References { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];

        public IReadOnlyList<StreamCandidate> Candidates { get; set; } = [];

        public Error? Failure { get; set; }

        public Task<Result<IReadOnlyList<StreamCandidate>>> GetMovieStreamsAsync(MovieStreamReference reference, CancellationToken cancellationToken = default)
        {
            Calls++;
            References.Add(reference);
            Tokens.Add(cancellationToken);

            return Task.FromResult(Failure is { } error
                ? Result.Failure<IReadOnlyList<StreamCandidate>>(error)
                : Result.Success(Candidates));
        }

        public Task<Result<IReadOnlyList<StreamCandidate>>> GetEpisodeStreamsAsync(EpisodeStreamReference reference, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Playback resolution is for movies only.");
    }

    private sealed class RecordingValidator : IMediaValidator
    {
        public Func<string, MediaValidationResult> Verdict { get; set; } = _ => throw new InvalidOperationException("The media validator was not expected to be called.");

        public List<string> Calls { get; } = [];

        public List<MediaValidationReference> References { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];

        public Task<MediaValidationResult> ValidateAsync(StreamCandidate candidate, MediaValidationReference reference, CancellationToken cancellationToken = default)
        {
            Calls.Add(candidate.Name);
            References.Add(reference);
            Tokens.Add(cancellationToken);
            return Task.FromResult(Verdict(candidate.Name));
        }
    }

    /// <summary>Everything a log sink could see at any level: formatted message, every structured property, full exception text.</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<string> _entries = [];

        public IReadOnlyList<string> Entries => _entries;

        public string AllText => string.Join('\n', _entries);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var entry = new StringBuilder();
            entry.Append('[').Append(logLevel).Append("] ").Append(formatter(state, exception));

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

            _entries.Add(entry.ToString());
        }
    }
}
