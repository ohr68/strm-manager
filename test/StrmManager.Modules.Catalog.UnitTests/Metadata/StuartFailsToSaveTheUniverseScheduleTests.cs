using System.Text.Json;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Metadata;
using StrmManager.Modules.Catalog.Domain.Episodes;
using StrmManager.Modules.Catalog.Domain.Shared;
using StrmManager.Modules.Catalog.Infrastructure.Metadata.Cinemeta;

namespace StrmManager.Modules.Catalog.UnitTests.Metadata;

/// <summary>
/// The real-world case called out explicitly in the Phase 2 brief: "Stuart Fails to
/// Save the Universe" (tt27497393), where S01E09 airs at exactly 2026-09-18T05:00:00Z
/// and S01E10 a week later. Proves the mapper preserves the exact UTC instant (not just
/// the date) and that Episode.Schedule/TryBecomeEligible transition at that exact
/// boundary - using controlled, literal UTC timestamps throughout, never the machine's
/// clock.
/// </summary>
public class StuartFailsToSaveTheUniverseScheduleTests
{
    private static readonly DateTime Episode09ReleaseAtUtc = new(2026, 9, 18, 5, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Episode10ReleaseAtUtc = new(2026, 9, 25, 5, 0, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private static SeriesMetadata LoadStuartMetadata()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Metadata", "Fixtures", "stuart-continuing.json");
        string json = File.ReadAllText(path);
        CinemetaSeriesResponseDto? response = JsonSerializer.Deserialize<CinemetaSeriesResponseDto>(json, SerializerOptions);

        Result<SeriesMetadata> result = CinemetaMetadataMapper.Map(response!.Meta!);
        Assert.True(result.IsSuccess);

        return result.Value;
    }

    [Fact]
    public void Fixture_PreservesExactReleaseInstantsForEpisodes09And10()
    {
        SeriesMetadata metadata = LoadStuartMetadata();

        EpisodeMetadata episode09 = metadata.Episodes.Single(e => e.EpisodeNumber == 9);
        EpisodeMetadata episode10 = metadata.Episodes.Single(e => e.EpisodeNumber == 10);

        Assert.Equal(Episode09ReleaseAtUtc, episode09.ReleaseAtUtc);
        Assert.Equal(Episode10ReleaseAtUtc, episode10.ReleaseAtUtc);
    }

    [Fact]
    public void OneSecondBeforeRelease_Episode09_IsStillScheduled()
    {
        SeriesMetadata metadata = LoadStuartMetadata();
        EpisodeMetadata episode09Metadata = metadata.Episodes.Single(e => e.EpisodeNumber == 9);

        DateTime controlledUtcNow = new(2026, 9, 18, 4, 59, 59, DateTimeKind.Utc);

        Episode episode09 = Episode.Schedule(
            Guid.NewGuid(),
            episode09Metadata.ExternalId,
            episode09Metadata.Title,
            episode09Metadata.SeasonNumber,
            episode09Metadata.EpisodeNumber,
            episode09Metadata.Runtime,
            episode09Metadata.ReleaseAtUtc!.Value,
            controlledUtcNow);

        Assert.Equal(MediaStatus.Scheduled, episode09.Status);
    }

    [Fact]
    public void AtExactReleaseInstant_Episode09_BecomesEligible_Episode10_RemainsScheduled()
    {
        SeriesMetadata metadata = LoadStuartMetadata();
        EpisodeMetadata episode09Metadata = metadata.Episodes.Single(e => e.EpisodeNumber == 9);
        EpisodeMetadata episode10Metadata = metadata.Episodes.Single(e => e.EpisodeNumber == 10);

        // Both episodes were originally scheduled well before either aired.
        DateTime creationUtcNow = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

        Episode episode09 = Episode.Schedule(
            Guid.NewGuid(), episode09Metadata.ExternalId, episode09Metadata.Title,
            episode09Metadata.SeasonNumber, episode09Metadata.EpisodeNumber,
            episode09Metadata.Runtime, episode09Metadata.ReleaseAtUtc!.Value, creationUtcNow);

        Episode episode10 = Episode.Schedule(
            Guid.NewGuid(), episode10Metadata.ExternalId, episode10Metadata.Title,
            episode10Metadata.SeasonNumber, episode10Metadata.EpisodeNumber,
            episode10Metadata.Runtime, episode10Metadata.ReleaseAtUtc!.Value, creationUtcNow);

        Assert.Equal(MediaStatus.Scheduled, episode09.Status);
        Assert.Equal(MediaStatus.Scheduled, episode10.Status);

        // The scheduler ticks at exactly the boundary instant for E09.
        DateTime boundaryUtcNow = new(2026, 9, 18, 5, 0, 0, DateTimeKind.Utc);

        bool episode09BecameEligible = episode09.TryBecomeEligible(boundaryUtcNow);
        bool episode10BecameEligible = episode10.TryBecomeEligible(boundaryUtcNow);

        Assert.True(episode09BecameEligible);
        Assert.Equal(MediaStatus.Pending, episode09.Status);

        Assert.False(episode10BecameEligible);
        Assert.Equal(MediaStatus.Scheduled, episode10.Status);
    }
}
