using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Playback;
using StrmManager.Modules.Catalog.Domain.Episodes;
using StrmManager.Modules.Catalog.Domain.Shared;
using StrmManager.Modules.Catalog.Domain.SourceAttempts;
using StrmManager.Modules.MediaProcessing.Application.Streams;
using StrmManager.Modules.MediaProcessing.Application.Validation;

namespace StrmManager.Modules.Catalog.UnitTests.Playback;

public class EpisodePlaybackResolverTests
{
    private const string ExternalId = "episode-207";
    private const int SeasonNumber = 2;
    private const int EpisodeNumber = 7;

    private static readonly TimeSpan Runtime = TimeSpan.FromMinutes(48);

    private const string CanaryHost = "episode-canary.invalid";
    private const string PathSecret = "EPISODE-PATH-SECRET";
    private const string QuerySecret = "EPISODE-QUERY-SECRET";
    private const string RawProviderText = "EPISODE-PROVIDER-SECRET";
    private const string RawValidatorText = "EPISODE-VALIDATOR-SECRET";

    private static readonly string[] AllSecrets =
    [
        CanaryHost,
        PathSecret,
        QuerySecret,
        RawProviderText,
        RawValidatorText,
    ];

    private static readonly DateTime Seeded =
        new(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);

    private static string UrlFor(string label) =>
        $"https://{CanaryHost}/hls/{PathSecret}/{label}.m3u8?sig={QuerySecret}-{label}";

    private static StreamCandidate Compatible(string label) =>
        new(
            "FrostStream",
            $"{label} S02E07",
            null,
            UrlFor(label));

    private static StreamCandidate Conflicting(string label) =>
        new(
            "FrostStream",
            $"{label} S02E08",
            null,
            UrlFor(label));

    private static MediaValidationResult Approved() =>
        MediaValidationResult.ForApproval(
            Runtime,
            Runtime,
            0,
            "h264",
            "aac");

    private static MediaValidationResult Bad() =>
        MediaValidationResult.ForRejection(
            SourceAttemptResult.InvalidMedia,
            $"{RawValidatorText} {UrlFor("echoed")}",
            TimeSpan.FromMinutes(5),
            Runtime,
            89.5,
            "mpeg4",
            null);

    private static Episode EpisodeIn(MediaStatus status)
    {
        DateTime release =
            status == MediaStatus.Scheduled
                ? Seeded.AddDays(30)
                : Seeded.AddDays(-30);

        Episode episode = Episode.Schedule(
            Guid.NewGuid(),
            ExternalId,
            "Episode Seven",
            SeasonNumber,
            EpisodeNumber,
            Runtime,
            release,
            Seeded);

        switch (status)
        {
            case MediaStatus.Scheduled:
            case MediaStatus.Pending:
                break;

            case MediaStatus.Searching:
                episode.StartSearching(Seeded);
                break;

            case MediaStatus.Validating:
                episode.StartSearching(Seeded);
                episode.StartValidating(Seeded);
                break;

            case MediaStatus.Completed:
                episode.StartSearching(Seeded);
                episode.MarkCompleted(Seeded);
                break;

            case MediaStatus.Unavailable:
                episode.StartSearching(Seeded);
                episode.MarkUnavailable(
                    Seeded,
                    Seeded.AddHours(6),
                    "no candidate passed validation");
                break;

            case MediaStatus.Error:
                episode.StartSearching(Seeded);
                episode.MarkError(
                    Seeded,
                    "stream provider unavailable",
                    Seeded.AddMinutes(15));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }

        Assert.Equal(status, episode.Status);

        return episode;
    }

    private static object Snapshot(Episode episode) =>
        (
            episode.Id,
            episode.SeasonId,
            episode.ExternalId,
            episode.Title,
            episode.SeasonNumber,
            episode.EpisodeNumber,
            episode.Runtime,
            episode.ReleaseAtUtc,
            episode.Status,
            episode.LastAttemptAtUtc,
            episode.NextAttemptAtUtc,
            episode.AttemptCount,
            episode.LastError,
            episode.CreatedAtUtc,
            episode.UpdatedAtUtc
        );

    private sealed class Harness
    {
        public Harness(Episode? episode)
        {
            Episode = episode;
            Repository = new FakeEpisodeRepository(episode);

            Resolver = new EpisodePlaybackResolver(
                Repository,
                Provider,
                Validator,
                Logger);
        }

        public Episode? Episode { get; }

        public FakeEpisodeRepository Repository { get; }

        public FakeStreamProvider Provider { get; } = new();

        public RecordingValidator Validator { get; } = new();

        public CapturingLogger<EpisodePlaybackResolver> Logger { get; } = new();

        public EpisodePlaybackResolver Resolver { get; }

        public Task<PlaybackResolutionResult> ResolveAsync(
            CancellationToken cancellationToken = default) =>
            Resolver.ResolveAsync(
                Episode?.Id ?? Guid.NewGuid(),
                cancellationToken);

        public string EverythingObservable(PlaybackResolutionResult result) =>
            string.Join(
                '\n',
                result.ToString(),
                JsonSerializer.Serialize(result, result.GetType()),
                Logger.AllText);
    }

    private static void AssertNoSecrets(string text)
    {
        foreach (string secret in AllSecrets)
        {
            Assert.DoesNotContain(
                secret,
                text,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task UnknownEpisode_IsNotFound_AndProviderIsNotCalled()
    {
        var h = new Harness(episode: null);

        PlaybackResolutionResult result = await h.ResolveAsync();

        Assert.IsType<PlaybackResolutionResult.NotFound>(result);
        Assert.Equal(0, h.Provider.EpisodeCalls);
        Assert.Empty(h.Validator.Calls);
    }

    [Theory]
    [InlineData(MediaStatus.Pending)]
    [InlineData(MediaStatus.Scheduled)]
    [InlineData(MediaStatus.Searching)]
    [InlineData(MediaStatus.Validating)]
    [InlineData(MediaStatus.Unavailable)]
    [InlineData(MediaStatus.Error)]
    public async Task EpisodeThatIsNotCompleted_IsNotFound_AndProviderIsNotCalled(
        MediaStatus status)
    {
        var h = new Harness(EpisodeIn(status));

        PlaybackResolutionResult result = await h.ResolveAsync();

        Assert.IsType<PlaybackResolutionResult.NotFound>(result);
        Assert.Equal(0, h.Provider.EpisodeCalls);
        Assert.Empty(h.Validator.Calls);
    }

    [Fact]
    public async Task CompletedEpisode_AsksProviderForFreshCandidates()
    {
        var h = new Harness(EpisodeIn(MediaStatus.Completed));

        h.Provider.Candidates = [Compatible("candidate")];
        h.Validator.Verdict = _ => Approved();

        await h.ResolveAsync();

        Assert.Equal(1, h.Provider.EpisodeCalls);
    }

    [Fact]
    public async Task ProviderReceivesEpisodeIdentityAndMetadata()
    {
        Episode episode = EpisodeIn(MediaStatus.Completed);
        var h = new Harness(episode);

        h.Provider.Candidates = [];

        await h.ResolveAsync();

        Assert.Equal(
            new EpisodeStreamReference(
                ExternalId,
                SeasonNumber,
                EpisodeNumber,
                "Episode Seven",
                Runtime),
            Assert.Single(h.Provider.EpisodeReferences));
    }

    [Fact]
    public async Task ProviderFailure_IsUnavailable_WithoutProviderText()
    {
        var h = new Harness(EpisodeIn(MediaStatus.Completed));

        h.Provider.Failure = Error.Failure(
            "Streams.InvalidResponse",
            $"{RawProviderText} {UrlFor("provider")}");

        PlaybackResolutionResult result = await h.ResolveAsync();

        Assert.Equal(
            new PlaybackResolutionResult.Unavailable(
                PlaybackUnavailableReason.ProviderFailure),
            result);

        Assert.Empty(h.Validator.Calls);
        Assert.Contains("Streams.InvalidResponse", h.Logger.AllText);
        AssertNoSecrets(h.EverythingObservable(result));
    }

    [Fact]
    public async Task ZeroCandidates_IsUnavailable()
    {
        var h = new Harness(EpisodeIn(MediaStatus.Completed));

        h.Provider.Candidates = [];

        PlaybackResolutionResult result = await h.ResolveAsync();

        Assert.Equal(
            new PlaybackResolutionResult.Unavailable(
                PlaybackUnavailableReason.NoCandidates),
            result);

        Assert.Empty(h.Validator.Calls);
    }

    [Fact]
    public async Task FirstApprovedCandidate_IsResolved()
    {
        var h = new Harness(EpisodeIn(MediaStatus.Completed));

        StreamCandidate winner = Compatible("winner");

        h.Provider.Candidates =
        [
            Compatible("bad"),
            winner,
            Compatible("later"),
        ];

        h.Validator.Verdict =
            name => name.StartsWith("bad", StringComparison.Ordinal)
                ? Bad()
                : Approved();

        PlaybackResolutionResult result = await h.ResolveAsync();

        var resolved =
            Assert.IsType<PlaybackResolutionResult.Resolved>(result);

        Assert.Equal(winner.Url, resolved.Location.Reveal());
        Assert.Equal("FrostStream", resolved.Provider);
        Assert.Equal(winner.Name, resolved.SourceName);

        Assert.Equal(2, h.Validator.Calls.Count);
    }

    [Fact]
    public async Task ConflictingEpisodeIdentity_NeverReachesValidator()
    {
        var h = new Harness(EpisodeIn(MediaStatus.Completed));

        h.Provider.Candidates = [Conflicting("wrong")];
        h.Validator.Verdict = _ => Approved();

        PlaybackResolutionResult result = await h.ResolveAsync();

        Assert.Empty(h.Validator.Calls);

        Assert.Equal(
            new PlaybackResolutionResult.Unavailable(
                PlaybackUnavailableReason.NoApprovedCandidate),
            result);
    }

    [Fact]
    public async Task ValidatorReceivesEpisodeRuntime()
    {
        var h = new Harness(EpisodeIn(MediaStatus.Completed));

        h.Provider.Candidates = [Compatible("candidate")];
        h.Validator.Verdict = _ => Approved();

        await h.ResolveAsync();

        Assert.Equal(
            [new MediaValidationReference(Runtime)],
            h.Validator.References);
    }

    [Fact]
    public async Task CancellationPropagatesToEveryCollaborator()
    {
        var h = new Harness(EpisodeIn(MediaStatus.Completed));

        h.Provider.Candidates =
        [
            Compatible("first"),
            Compatible("second"),
        ];

        using var cts = new CancellationTokenSource();

        h.Validator.Verdict = _ =>
        {
            cts.Cancel();
            return Bad();
        };

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => h.ResolveAsync(cts.Token));

        Assert.Single(h.Validator.Calls);
        Assert.Equal([cts.Token], h.Repository.Tokens);
        Assert.Equal([cts.Token], h.Provider.Tokens);
        Assert.Equal([cts.Token], h.Validator.Tokens);
    }

    [Theory]
    [InlineData("resolved")]
    [InlineData("no-candidates")]
    [InlineData("none-approved")]
    [InlineData("provider-failure")]
    public async Task Resolution_NeverChangesTheEpisode(string outcome)
    {
        Episode episode = EpisodeIn(MediaStatus.Completed);
        object before = Snapshot(episode);

        var h = new Harness(episode);

        switch (outcome)
        {
            case "resolved":
                h.Provider.Candidates = [Compatible("candidate")];
                h.Validator.Verdict = _ => Approved();
                break;

            case "no-candidates":
                h.Provider.Candidates = [];
                break;

            case "none-approved":
                h.Provider.Candidates = [Compatible("candidate")];
                h.Validator.Verdict = _ => Bad();
                break;

            default:
                h.Provider.Failure =
                    Error.Failure(
                        "Streams.Timeout",
                        $"{RawProviderText} {UrlFor("timeout")}");
                break;
        }

        await h.ResolveAsync();

        Assert.Equal(before, Snapshot(episode));
        Assert.Equal(0, h.Repository.Inserts);
    }

    [Fact]
    public void Resolver_HasNoDependencyThatCanPersistProcessingState()
    {
        Type[] parameters =
            typeof(EpisodePlaybackResolver)
                .GetConstructors()
                .Single()
                .GetParameters()
                .Select(parameter => parameter.ParameterType)
                .ToArray();

        Assert.Equal(
            [
                typeof(IEpisodeRepository),
                typeof(IStreamProvider),
                typeof(IMediaValidator),
                typeof(ILogger<EpisodePlaybackResolver>),
            ],
            parameters);
    }

    [Fact]
    public async Task ResolvedResult_NeverPrintsProviderUrl()
    {
        var h = new Harness(EpisodeIn(MediaStatus.Completed));

        StreamCandidate winner = Compatible("winner");

        h.Provider.Candidates = [winner];
        h.Validator.Verdict = _ => Approved();

        PlaybackResolutionResult result = await h.ResolveAsync();

        var resolved =
            Assert.IsType<PlaybackResolutionResult.Resolved>(result);

        Assert.Equal(winner.Url, resolved.Location.Reveal());

        string observable = string.Join(
            '\n',
            resolved.ToString(),
            resolved.Location.ToString(),
            $"{resolved}",
            $"{resolved.Location}",
            JsonSerializer.Serialize(resolved),
            JsonSerializer.Serialize(resolved.Location),
            h.Logger.AllText);

        AssertNoSecrets(observable);
    }

    private sealed class FakeEpisodeRepository(Episode? episode)
        : IEpisodeRepository
    {
        public List<CancellationToken> Tokens { get; } = [];

        public int Inserts { get; private set; }

        public Task<Episode?> GetAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            Tokens.Add(cancellationToken);

            return Task.FromResult(
                episode is not null && episode.Id == id
                    ? episode
                    : null);
        }

        public void Insert(Episode episode) => Inserts++;

        public Task<Episode?> GetBySeasonAndNumberAsync(
            Guid seasonId,
            int episodeNumber,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Episode>> GetBySeasonAsync(
            Guid seasonId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Episode>> GetScheduledDueAsync(
            DateTime utcNow,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Episode>> GetRetryableAsync(
            DateTime utcNow,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Episode>> GetStaleProcessingAsync(
            DateTime staleThresholdUtc,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Episode>> GetPendingForProcessingAsync(
            int limit,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<MediaStatus, int>> GetStatusCountsAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Episode>> GetByStatusAsync(
            MediaStatus? status,
            int skip,
            int take,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeStreamProvider : IStreamProvider
    {
        public int EpisodeCalls { get; private set; }

        public List<EpisodeStreamReference> EpisodeReferences { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];

        public IReadOnlyList<StreamCandidate> Candidates { get; set; } = [];

        public Error? Failure { get; set; }

        public Task<Result<IReadOnlyList<StreamCandidate>>>
            GetEpisodeStreamsAsync(
                EpisodeStreamReference reference,
                CancellationToken cancellationToken = default)
        {
            EpisodeCalls++;
            EpisodeReferences.Add(reference);
            Tokens.Add(cancellationToken);

            return Task.FromResult(
                Failure is { } error
                    ? Result.Failure<IReadOnlyList<StreamCandidate>>(error)
                    : Result.Success(Candidates));
        }

        public Task<Result<IReadOnlyList<StreamCandidate>>>
            GetMovieStreamsAsync(
                MovieStreamReference reference,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException(
                "Episode playback must not perform movie discovery.");
    }

    private sealed class RecordingValidator : IMediaValidator
    {
        public Func<string, MediaValidationResult> Verdict { get; set; } =
            _ => throw new InvalidOperationException(
                "The media validator was not expected to be called.");

        public List<string> Calls { get; } = [];

        public List<MediaValidationReference> References { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];

        public Task<MediaValidationResult> ValidateAsync(
            StreamCandidate candidate,
            MediaValidationReference reference,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(candidate.Name);
            References.Add(reference);
            Tokens.Add(cancellationToken);

            return Task.FromResult(Verdict(candidate.Name));
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<string> _entries = [];

        public IReadOnlyList<string> Entries => _entries;

        public string AllText => string.Join('\n', _entries);

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var entry = new StringBuilder();

            entry
                .Append('[')
                .Append(logLevel)
                .Append("] ")
                .Append(formatter(state, exception));

            if (state is IEnumerable<KeyValuePair<string, object?>> properties)
            {
                foreach (KeyValuePair<string, object?> property in properties)
                {
                    entry
                        .Append(" | ")
                        .Append(property.Key)
                        .Append('=')
                        .Append(property.Value);
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
