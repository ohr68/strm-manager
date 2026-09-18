using System.Text.Json;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Metadata;
using StrmManager.Modules.Catalog.Domain.Series;
using StrmManager.Modules.Catalog.Infrastructure.Metadata.Cinemeta;

namespace StrmManager.Modules.Catalog.UnitTests.Metadata;

public class CinemetaMetadataMapperTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private static CinemetaMetaDto LoadFixtureMeta(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Metadata", "Fixtures", fileName);
        string json = File.ReadAllText(path);
        CinemetaMetaResponseDto? response = JsonSerializer.Deserialize<CinemetaMetaResponseDto>(json, SerializerOptions);

        return response?.Meta ?? throw new InvalidOperationException($"Fixture '{fileName}' has no meta object.");
    }

    [Fact]
    public void Map_ContinuingSeriesWithFutureEpisodes_MapsCoreFieldsAndStatus()
    {
        CinemetaMetaDto meta = LoadFixtureMeta("stuart-continuing.json");

        Result<SeriesMetadata> result = CinemetaMetadataMapper.Map(meta);

        Assert.True(result.IsSuccess);
        SeriesMetadata metadata = result.Value;
        Assert.Equal("Stuart Fails to Save the Universe", metadata.Title);
        Assert.Equal(2026, metadata.Year);
        Assert.Equal(SeriesStatus.Active, metadata.Status);
        Assert.Equal("tt27497393", metadata.ExternalIds.ImdbId);
        Assert.Equal(10, metadata.Episodes.Count);
        Assert.Equal(TimeSpan.FromMinutes(20), metadata.Episodes[0].Runtime);
    }

    [Fact]
    public void Map_EndedSeries_MapsStatusToEndedAndParsesStartYear()
    {
        CinemetaMetaDto meta = LoadFixtureMeta("ended-series.json");

        Result<SeriesMetadata> result = CinemetaMetadataMapper.Map(meta);

        Assert.True(result.IsSuccess);
        Assert.Equal(SeriesStatus.Ended, result.Value.Status);
        Assert.Equal(2018, result.Value.Year);
    }

    [Fact]
    public void Map_UnrecognizedStatusString_FallsBackToActive()
    {
        var meta = new CinemetaMetaDto
        {
            Id = "tt0000001",
            Name = "Unrecognized Status Show",
            ReleaseInfo = "2020-",
            Status = "SomeFutureCinemetaStatusValue",
            Videos = [],
        };

        Result<SeriesMetadata> result = CinemetaMetadataMapper.Map(meta);

        Assert.True(result.IsSuccess);
        Assert.Equal(SeriesStatus.Active, result.Value.Status);
    }

    [Fact]
    public void Map_FirstAired_TakesPrecedenceOverReleased_AndPreservesExactUtcTimestamp()
    {
        CinemetaMetaDto meta = LoadFixtureMeta("stuart-continuing.json");

        Result<SeriesMetadata> result = CinemetaMetadataMapper.Map(meta);

        EpisodeMetadata episodeNine = result.Value.Episodes.Single(e => e.EpisodeNumber == 9);

        Assert.Equal(new DateTime(2026, 9, 18, 5, 0, 0, DateTimeKind.Utc), episodeNine.ReleaseAtUtc);
        Assert.Equal(DateTimeKind.Utc, episodeNine.ReleaseAtUtc!.Value.Kind);
    }

    [Fact]
    public void Map_MissingFirstAired_FallsBackToReleased()
    {
        CinemetaMetaDto meta = LoadFixtureMeta("missing-first-aired-fallback-released.json");

        Result<SeriesMetadata> result = CinemetaMetadataMapper.Map(meta);

        EpisodeMetadata episode = Assert.Single(result.Value.Episodes);
        Assert.Equal(new DateTime(2024, 3, 1, 5, 0, 0, DateTimeKind.Utc), episode.ReleaseAtUtc);
    }

    [Fact]
    public void Map_MissingBothReleaseDates_LeavesReleaseAtUtcNull_DoesNotInventADate()
    {
        CinemetaMetaDto meta = LoadFixtureMeta("missing-both-release-dates.json");

        Result<SeriesMetadata> result = CinemetaMetadataMapper.Map(meta);

        EpisodeMetadata episodeOne = result.Value.Episodes.Single(e => e.EpisodeNumber == 1);
        Assert.Null(episodeOne.ReleaseAtUtc);

        EpisodeMetadata episodeTwo = result.Value.Episodes.Single(e => e.EpisodeNumber == 2);
        Assert.NotNull(episodeTwo.ReleaseAtUtc);
    }

    [Fact]
    public void Map_SeasonZero_IsPreservedNotDiscarded()
    {
        CinemetaMetaDto meta = LoadFixtureMeta("season-zero-specials.json");

        Result<SeriesMetadata> result = CinemetaMetadataMapper.Map(meta);

        Assert.Contains(result.Value.Episodes, episode => episode.SeasonNumber == 0);
    }

    [Fact]
    public void Map_MalformedOptionalMetadata_DoesNotFail_UsesSafeDefaults()
    {
        CinemetaMetaDto meta = LoadFixtureMeta("malformed-metadata.json");

        Result<SeriesMetadata> result = CinemetaMetadataMapper.Map(meta);

        Assert.True(result.IsSuccess);
        EpisodeMetadata untitled = result.Value.Episodes.Single(episode => episode.EpisodeNumber == 1);
        Assert.Equal("Episode 1", untitled.Title);
        Assert.Null(untitled.Runtime);
        Assert.Equal(SeriesStatus.Active, result.Value.Status);
    }

    [Fact]
    public void Map_MissingSeriesName_ReturnsInvalidResponse()
    {
        var meta = new CinemetaMetaDto { Id = "tt0000002", Name = null, ReleaseInfo = "2020-" };

        Result<SeriesMetadata> result = CinemetaMetadataMapper.Map(meta);

        Assert.True(result.IsFailure);
        Assert.Equal("Metadata.InvalidResponse", result.Error.Code);
    }

    [Fact]
    public void Map_MissingReleaseYear_ReturnsInvalidResponse()
    {
        var meta = new CinemetaMetaDto { Id = "tt0000003", Name = "No Year Show", ReleaseInfo = null };

        Result<SeriesMetadata> result = CinemetaMetadataMapper.Map(meta);

        Assert.True(result.IsFailure);
        Assert.Equal("Metadata.InvalidResponse", result.Error.Code);
    }
}
