using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Episodes.ProcessEpisode;
using StrmManager.Modules.Catalog.Application.Maintenance;
using StrmManager.Modules.Catalog.Application.Metadata;
using StrmManager.Modules.Catalog.Domain.Episodes;
using StrmManager.Modules.Catalog.Domain.Series;
using StrmManager.Modules.Catalog.Domain.Shared;
using StrmManager.Modules.Catalog.Infrastructure.Database;
using StrmManager.Modules.Catalog.IntegrationTests.Infrastructure;
using StrmManager.Modules.MediaProcessing.Application.Streams;
using StrmManager.Modules.MediaProcessing.Application.Validation;
using SeriesEntity = StrmManager.Modules.Catalog.Domain.Series.Series;

namespace StrmManager.Modules.Catalog.IntegrationTests.Scheduling;

/// <summary>
/// Deterministic coverage of RunCatalogMaintenanceCommand (release eligibility, retry,
/// stale-processing recovery, metadata-refresh scheduling) via FakeTimeProvider - never
/// the real wall clock, never a running BackgroundService (ApiWebApplicationFactory sets
/// Scheduling:Enabled=false; this exercises the same use case the workers call).
///
/// Deliberately NOT IClassFixture-shared - RunCatalogMaintenanceCommand operates over
/// the *entire* catalog (unlike the single-episode-scoped tests elsewhere), so every
/// test needs its own fresh database; a shared fixture would let series left behind by
/// an earlier test in the class be picked up by a later test's maintenance run.
/// </summary>
public class CatalogMaintenanceTests : IDisposable
{
    private static readonly DateTime BaseUtcNow = new(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc);

    private readonly ApiWebApplicationFactory _factory = new();
    private readonly HttpClient _client;
    private readonly FakeTimeProvider _fakeTimeProvider;
    private readonly FakeMetadataProvider _fakeMetadataProvider;
    private readonly FakeStreamProvider _fakeStreamProvider;
    private readonly FakeMediaValidator _fakeMediaValidator;
    private readonly IServiceProvider _services;

    public CatalogMaintenanceTests()
    {
        _fakeTimeProvider = new FakeTimeProvider(BaseUtcNow);
        _fakeMetadataProvider = new FakeMetadataProvider();
        _fakeStreamProvider = new FakeStreamProvider();
        _fakeMediaValidator = new FakeMediaValidator();

        WebApplicationFactory<Program> isolatedFactory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<TimeProvider>(_fakeTimeProvider);
                services.AddSingleton<IMetadataProvider>(_fakeMetadataProvider);
                services.AddSingleton<IStreamProvider>(_fakeStreamProvider);
                services.AddSingleton<IMediaValidator>(_fakeMediaValidator);
            }));

        _client = isolatedFactory.CreateClient();
        _services = isolatedFactory.Services;
    }

    public void Dispose()
    {
        _factory.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<CatalogMaintenanceResult> RunMaintenanceAsync()
    {
        using IServiceScope scope = _services.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<RunCatalogMaintenanceCommand, CatalogMaintenanceResult>>();

        Result<CatalogMaintenanceResult> result = await handler.Handle(new RunCatalogMaintenanceCommand(), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private async Task<Guid> ProcessEpisodeDirectlyAsync(Guid episodeId)
    {
        using IServiceScope scope = _services.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<ProcessEpisodeCommand, ProcessEpisodeResult>>();

        Result<ProcessEpisodeResult> result = await handler.Handle(new ProcessEpisodeCommand(episodeId), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return episodeId;
    }

    private async Task<(Guid SeriesId, Guid EpisodeId)> AddSeriesWithSingleEpisodeAsync(
        string imdbId, DateTime releaseAtUtc, TimeSpan? runtime = null)
    {
        runtime ??= TimeSpan.FromMinutes(52);

        _fakeMetadataProvider.Handler = _ => new SeriesMetadata(
            new ExternalIds(imdbId, null, null),
            "Paradise",
            null,
            2025,
            SeriesStatus.Active,
            [new EpisodeMetadata($"{imdbId}:1:1", "Episode One", SeasonNumber: 1, EpisodeNumber: 1, Runtime: runtime, ReleaseAtUtc: releaseAtUtc)]);

        var request = new
        {
            ImdbId = imdbId,
            TmdbId = (string?)null,
            TvdbId = (string?)null,
            Title = "Placeholder",
            OriginalTitle = (string?)null,
            Year = 2025,
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/series", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CreatedResponse>();
        Assert.NotNull(created);

        using IServiceScope scope = _services.CreateScope();
        CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Episode episode = await context.Set<Episode>().SingleAsync(e => e.ExternalId == $"{imdbId}:1:1");

        return (created.Id, episode.Id);
    }

    private async Task<Episode> GetEpisodeAsync(Guid episodeId)
    {
        using IServiceScope scope = _services.CreateScope();
        CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        return await context.Set<Episode>().SingleAsync(e => e.Id == episodeId);
    }

    // --- Release eligibility (sections 8/35/55) ---

    [Fact]
    public async Task RunMaintenance_ScheduledEpisodeNotYetReleased_StaysScheduled()
    {
        (_, Guid episodeId) = await AddSeriesWithSingleEpisodeAsync("tt00000301", BaseUtcNow.AddDays(1));

        _fakeTimeProvider.SetUtcNow(BaseUtcNow.AddDays(1).AddSeconds(-1));
        CatalogMaintenanceResult result = await RunMaintenanceAsync();

        Assert.Equal(0, result.EpisodesReleased);
        Episode episode = await GetEpisodeAsync(episodeId);
        Assert.Equal(MediaStatus.Scheduled, episode.Status);
    }

    [Fact]
    public async Task RunMaintenance_ScheduledEpisodeReleased_BecomesPendingAndIsThenProcessedToCompletion()
    {
        // The core reason Phase 4 exists: a future episode is released, discovered, and
        // processed with no manual intervention - only the maintenance/processing use
        // cases being called (here directly, by the same worker that runs them for real).
        DateTime releaseAtUtc = new(2026, 9, 18, 5, 0, 0, DateTimeKind.Utc);
        (_, Guid episodeId) = await AddSeriesWithSingleEpisodeAsync("tt00000302", releaseAtUtc, TimeSpan.FromMinutes(52));

        _fakeTimeProvider.SetUtcNow(releaseAtUtc.AddSeconds(-1));
        CatalogMaintenanceResult beforeRelease = await RunMaintenanceAsync();
        Assert.Equal(0, beforeRelease.EpisodesReleased);
        Assert.Equal(MediaStatus.Scheduled, (await GetEpisodeAsync(episodeId)).Status);

        _fakeTimeProvider.SetUtcNow(releaseAtUtc);
        CatalogMaintenanceResult atRelease = await RunMaintenanceAsync();
        Assert.Equal(1, atRelease.EpisodesReleased);
        Assert.Equal(MediaStatus.Pending, (await GetEpisodeAsync(episodeId)).Status);

        var candidate = new StreamCandidate("FrostStream", "Source S01E01", null, "https://media.example.test/only");
        _fakeStreamProvider.Handler = _ => Result.Success<IReadOnlyList<StreamCandidate>>([candidate]);
        _fakeMediaValidator.Handler = (_, reference) => MediaValidationResult.ForApproval(TimeSpan.FromMinutes(52), reference.ExpectedRuntime, 0, "h264", "aac");

        await ProcessEpisodeDirectlyAsync(episodeId);

        Episode episode = await GetEpisodeAsync(episodeId);
        Assert.Equal(MediaStatus.Completed, episode.Status);

        using IServiceScope scope = _services.CreateScope();
        CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        int strmFileCount = await context.Set<Domain.StrmFiles.StrmFile>().CountAsync(f => f.EpisodeId == episodeId);
        Assert.Equal(1, strmFileCount);
    }

    // --- Retry (sections 17/21/36) ---

    [Fact]
    public async Task RunMaintenance_UnavailableEpisodeNotYetDue_StaysUntouched()
    {
        (_, Guid episodeId) = await AddSeriesWithSingleEpisodeAsync("tt00000303", BaseUtcNow.AddDays(-1));
        await MarkUnavailableAsync(episodeId, nextAttemptAtUtc: BaseUtcNow.AddHours(1));

        CatalogMaintenanceResult result = await RunMaintenanceAsync();

        Assert.Equal(0, result.EpisodesRetried);
        Assert.Equal(MediaStatus.Unavailable, (await GetEpisodeAsync(episodeId)).Status);
    }

    [Fact]
    public async Task RunMaintenance_UnavailableEpisodeDue_ReturnsToPending()
    {
        (_, Guid episodeId) = await AddSeriesWithSingleEpisodeAsync("tt00000304", BaseUtcNow.AddDays(-1));
        await MarkUnavailableAsync(episodeId, nextAttemptAtUtc: BaseUtcNow.AddHours(-1));

        CatalogMaintenanceResult result = await RunMaintenanceAsync();

        Assert.Equal(1, result.EpisodesRetried);
        Assert.Equal(MediaStatus.Pending, (await GetEpisodeAsync(episodeId)).Status);
    }

    [Fact]
    public async Task RunMaintenance_RetryableErrorDue_ReturnsToPending()
    {
        (_, Guid episodeId) = await AddSeriesWithSingleEpisodeAsync("tt00000305", BaseUtcNow.AddDays(-1));
        await MarkErrorAsync(episodeId, nextAttemptAtUtc: BaseUtcNow.AddMinutes(-1));

        CatalogMaintenanceResult result = await RunMaintenanceAsync();

        Assert.Equal(1, result.EpisodesRetried);
        Assert.Equal(MediaStatus.Pending, (await GetEpisodeAsync(episodeId)).Status);
    }

    [Fact]
    public async Task RunMaintenance_NonRetryableError_StaysUntouchedAutomatically()
    {
        (_, Guid episodeId) = await AddSeriesWithSingleEpisodeAsync("tt00000306", BaseUtcNow.AddDays(-1));
        await MarkErrorAsync(episodeId, nextAttemptAtUtc: null);

        CatalogMaintenanceResult result = await RunMaintenanceAsync();

        Assert.Equal(0, result.EpisodesRetried);
        Assert.Equal(MediaStatus.Error, (await GetEpisodeAsync(episodeId)).Status);
    }

    // --- Stale-processing recovery (sections 15/16/37/39) ---

    [Fact]
    public async Task RunMaintenance_SearchingYoungerThanStaleThreshold_StaysUntouched()
    {
        (_, Guid episodeId) = await AddSeriesWithSingleEpisodeAsync("tt00000307", BaseUtcNow.AddDays(-1));
        await StartSearchingAsync(episodeId, startedAtUtc: BaseUtcNow.AddMinutes(-5)); // well under the 15m default threshold

        CatalogMaintenanceResult result = await RunMaintenanceAsync();

        Assert.Equal(0, result.EpisodesRecovered);
        Assert.Equal(MediaStatus.Searching, (await GetEpisodeAsync(episodeId)).Status);
    }

    [Fact]
    public async Task RunMaintenance_ValidatingOlderThanStaleThreshold_IsRecoveredToPending()
    {
        (_, Guid episodeId) = await AddSeriesWithSingleEpisodeAsync("tt00000308", BaseUtcNow.AddDays(-1));
        await StartSearchingAsync(episodeId, startedAtUtc: BaseUtcNow.AddMinutes(-30));
        await StartValidatingAsync(episodeId, startedAtUtc: BaseUtcNow.AddMinutes(-30)); // well past the 15m default threshold

        CatalogMaintenanceResult result = await RunMaintenanceAsync();

        Assert.Equal(1, result.EpisodesRecovered);
        Episode episode = await GetEpisodeAsync(episodeId);
        Assert.Equal(MediaStatus.Pending, episode.Status);
        Assert.Equal(0, episode.AttemptCount);
    }

    [Fact]
    public async Task InterruptedProcessing_RecoveredStaleEpisode_CanBeReprocessedToCompletion()
    {
        // The restart scenario from section 39: claimed (Searching persisted), the run
        // never reached a terminal outcome, the clock moves past the stale threshold,
        // maintenance recovers it, and it processes successfully from there.
        (_, Guid episodeId) = await AddSeriesWithSingleEpisodeAsync("tt00000309", BaseUtcNow.AddDays(-1), TimeSpan.FromMinutes(52));
        await StartSearchingAsync(episodeId, startedAtUtc: BaseUtcNow.AddMinutes(-30));

        CatalogMaintenanceResult recovery = await RunMaintenanceAsync();
        Assert.Equal(1, recovery.EpisodesRecovered);
        Assert.Equal(MediaStatus.Pending, (await GetEpisodeAsync(episodeId)).Status);

        var candidate = new StreamCandidate("FrostStream", "Source S01E01", null, "https://media.example.test/recovered");
        _fakeStreamProvider.Handler = _ => Result.Success<IReadOnlyList<StreamCandidate>>([candidate]);
        _fakeMediaValidator.Handler = (_, reference) => MediaValidationResult.ForApproval(TimeSpan.FromMinutes(52), reference.ExpectedRuntime, 0, "h264", "aac");

        await ProcessEpisodeDirectlyAsync(episodeId);

        Episode episode = await GetEpisodeAsync(episodeId);
        Assert.Equal(MediaStatus.Completed, episode.Status);

        using IServiceScope scope = _services.CreateScope();
        CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        int strmFileCount = await context.Set<Domain.StrmFiles.StrmFile>().CountAsync(f => f.EpisodeId == episodeId);
        Assert.Equal(1, strmFileCount); // exactly one - not duplicated by the interrupted first attempt
    }

    // --- Metadata refresh (sections 22-25/38) ---

    [Fact]
    public async Task RunMaintenance_ActiveSeriesRefreshNotDue_DoesNotCallProvider()
    {
        (Guid seriesId, _) = await AddSeriesWithSingleEpisodeAsync("tt00000310", BaseUtcNow.AddDays(-1));

        _fakeMetadataProvider.Handler = _ => throw new InvalidOperationException("Provider should not be called before NextMetadataRefreshAtUtc is due.");

        CatalogMaintenanceResult result = await RunMaintenanceAsync();

        Assert.Equal(0, result.SeriesMetadataRefreshed);

        using IServiceScope scope = _services.CreateScope();
        CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        SeriesEntity series = await context.Set<SeriesEntity>().SingleAsync(s => s.Id == seriesId);
        Assert.NotNull(series.NextMetadataRefreshAtUtc);
        Assert.True(series.NextMetadataRefreshAtUtc > BaseUtcNow);
    }

    [Fact]
    public async Task RunMaintenance_ActiveSeriesRefreshDue_CallsProviderAndDiscoversNewEpisode()
    {
        const string imdbId = "tt00000311";
        (Guid seriesId, _) = await AddSeriesWithSingleEpisodeAsync(imdbId, BaseUtcNow.AddDays(-1));

        // Move past the default 6h refresh interval and announce a second episode.
        _fakeTimeProvider.Advance(TimeSpan.FromHours(7));
        _fakeMetadataProvider.Handler = _ => new SeriesMetadata(
            new ExternalIds(imdbId, null, null), "Paradise", null, 2025, SeriesStatus.Active,
            [
                new EpisodeMetadata($"{imdbId}:1:1", "Episode One", 1, 1, TimeSpan.FromMinutes(52), BaseUtcNow.AddDays(-1)),
                new EpisodeMetadata($"{imdbId}:1:2", "Episode Two", 1, 2, TimeSpan.FromMinutes(52), BaseUtcNow.AddDays(30)),
            ]);

        CatalogMaintenanceResult result = await RunMaintenanceAsync();

        Assert.Equal(1, result.SeriesMetadataRefreshed);

        using IServiceScope scope = _services.CreateScope();
        CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        int episodeCount = await context.Set<Episode>().CountAsync(e => e.ExternalId.StartsWith(imdbId));
        Assert.Equal(2, episodeCount);

        SeriesEntity series = await context.Set<SeriesEntity>().SingleAsync(s => s.Id == seriesId);
        Assert.NotNull(series.LastMetadataRefreshAtUtc);
    }

    [Fact]
    public async Task RunMaintenance_EndedSeriesIsNeverAutomaticallyRefreshed()
    {
        const string imdbId = "tt00000312";
        (Guid seriesId, _) = await AddSeriesWithSingleEpisodeAsync(imdbId, BaseUtcNow.AddDays(-1));

        using (IServiceScope scope = _services.CreateScope())
        {
            CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            SeriesEntity series = await context.Set<SeriesEntity>().SingleAsync(s => s.Id == seriesId);
            series.UpdateMetadata(series.Title, series.OriginalTitle, series.Year, SeriesStatus.Ended, BaseUtcNow);
            await context.SaveChangesAsync();
        }

        _fakeTimeProvider.Advance(TimeSpan.FromDays(30));
        _fakeMetadataProvider.Handler = _ => throw new InvalidOperationException("Ended series must never be refreshed automatically.");

        CatalogMaintenanceResult result = await RunMaintenanceAsync();

        Assert.Equal(0, result.SeriesMetadataRefreshed);
    }

    [Fact]
    public async Task RunMaintenance_MetadataProviderFailure_LeavesExistingCatalogIntactAndReschedules()
    {
        const string imdbId = "tt00000313";
        (Guid seriesId, _) = await AddSeriesWithSingleEpisodeAsync(imdbId, BaseUtcNow.AddDays(-1));

        _fakeTimeProvider.Advance(TimeSpan.FromHours(7));
        _fakeMetadataProvider.Handler = _ => Result.Failure<SeriesMetadata>(MetadataProviderErrors.ProviderUnavailable("Cinemeta"));

        CatalogMaintenanceResult result = await RunMaintenanceAsync();

        Assert.Equal(0, result.SeriesMetadataRefreshed);
        Assert.Equal(1, result.SeriesMetadataRefreshFailed);

        using IServiceScope scope = _services.CreateScope();
        CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        int episodeCount = await context.Set<Episode>().CountAsync(e => e.ExternalId.StartsWith(imdbId));
        Assert.Equal(1, episodeCount); // untouched - the one episode from the original add is still there

        SeriesEntity series = await context.Set<SeriesEntity>().SingleAsync(s => s.Id == seriesId);
        Assert.True(series.NextMetadataRefreshAtUtc > _fakeTimeProvider.GetUtcNow()); // rescheduled, not stuck retrying immediately
    }

    private async Task MarkUnavailableAsync(Guid episodeId, DateTime? nextAttemptAtUtc)
    {
        using IServiceScope scope = _services.CreateScope();
        CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Episode episode = await context.Set<Episode>().SingleAsync(e => e.Id == episodeId);
        episode.StartSearching(BaseUtcNow);
        episode.MarkUnavailable(BaseUtcNow, nextAttemptAtUtc, "no streams returned");
        await context.SaveChangesAsync();
    }

    private async Task MarkErrorAsync(Guid episodeId, DateTime? nextAttemptAtUtc)
    {
        using IServiceScope scope = _services.CreateScope();
        CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Episode episode = await context.Set<Episode>().SingleAsync(e => e.Id == episodeId);
        episode.StartSearching(BaseUtcNow);
        episode.MarkError(BaseUtcNow, "technical failure", nextAttemptAtUtc);
        await context.SaveChangesAsync();
    }

    private async Task StartSearchingAsync(Guid episodeId, DateTime startedAtUtc)
    {
        using IServiceScope scope = _services.CreateScope();
        CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Episode episode = await context.Set<Episode>().SingleAsync(e => e.Id == episodeId);
        episode.StartSearching(startedAtUtc);
        await context.SaveChangesAsync();
    }

    private async Task StartValidatingAsync(Guid episodeId, DateTime startedAtUtc)
    {
        using IServiceScope scope = _services.CreateScope();
        CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Episode episode = await context.Set<Episode>().SingleAsync(e => e.Id == episodeId);
        episode.StartValidating(startedAtUtc);
        await context.SaveChangesAsync();
    }

    private sealed record CreatedResponse(Guid Id);
}
