using System.Text.Json;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Metadata;
using StrmManager.Modules.Catalog.Infrastructure.Metadata.Cinemeta;

namespace StrmManager.Modules.Catalog.UnitTests.Metadata;

public class CinemetaMovieMetadataMapperTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private static CinemetaMetaDto LoadFixtureMeta(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Metadata", "Fixtures", fileName);
        string json = File.ReadAllText(path);
        CinemetaMetaResponseDto? response = JsonSerializer.Deserialize<CinemetaMetaResponseDto>(json, SerializerOptions);

        return response?.Meta ?? throw new InvalidOperationException($"Fixture '{fileName}' has no meta object.");
    }

    private static CinemetaMetaDto ValidMeta() => new()
    {
        Id = "tt0000010",
        Name = "A Movie",
        ReleaseInfo = "2020",
        Released = "2020-05-01T00:00:00.000Z",
        Runtime = "100 min",
    };

    [Fact]
    public void MapMovie_ValidCinemetaMovie_MapsToProviderNeutralMetadata()
    {
        CinemetaMetaDto meta = LoadFixtureMeta("movie-released.json");

        Result<MovieMetadata> result = CinemetaMetadataMapper.MapMovie(meta);

        Assert.True(result.IsSuccess);
        MovieMetadata metadata = result.Value;
        Assert.Equal("tt0111161", metadata.ExternalIds.ImdbId);
        Assert.Null(metadata.ExternalIds.TmdbId);
        Assert.Null(metadata.ExternalIds.TvdbId);
        Assert.Equal("The Shawshank Redemption", metadata.Title);
        Assert.Equal(1994, metadata.Year);
        Assert.Equal(TimeSpan.FromMinutes(142), metadata.Runtime);
        Assert.Equal(new DateTime(1994, 10, 14, 0, 0, 0, DateTimeKind.Utc), metadata.ReleaseAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MapMovie_MissingOrBlankTitle_ReturnsInvalidResponse(string? name)
    {
        CinemetaMetaDto meta = ValidMeta();
        meta.Name = name;

        Result<MovieMetadata> result = CinemetaMetadataMapper.MapMovie(meta);

        Assert.True(result.IsFailure);
        Assert.Equal("Metadata.InvalidResponse", result.Error.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-year")]
    [InlineData("99")]
    public void MapMovie_MissingOrInvalidYear_ReturnsInvalidResponse(string? releaseInfo)
    {
        CinemetaMetaDto meta = ValidMeta();
        meta.ReleaseInfo = releaseInfo;

        Result<MovieMetadata> result = CinemetaMetadataMapper.MapMovie(meta);

        Assert.True(result.IsFailure);
        Assert.Equal("Metadata.InvalidResponse", result.Error.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void MapMovie_MissingProviderId_ReturnsInvalidResponse(string? id)
    {
        // The provider's own id becomes the canonical duplicate key - a movie without
        // one must never be persisted with a null IMDb id.
        CinemetaMetaDto meta = ValidMeta();
        meta.Id = id;

        Result<MovieMetadata> result = CinemetaMetadataMapper.MapMovie(meta);

        Assert.True(result.IsFailure);
        Assert.Equal("Metadata.InvalidResponse", result.Error.Code);
    }

    [Theory]
    [InlineData("142 min", 142)]
    [InlineData("95min", 95)]
    public void MapMovie_Runtime_IsMappedToMinutes(string runtime, int expectedMinutes)
    {
        CinemetaMetaDto meta = ValidMeta();
        meta.Runtime = runtime;

        Result<MovieMetadata> result = CinemetaMetadataMapper.MapMovie(meta);

        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), result.Value.Runtime);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    public void MapMovie_MissingOrUnparsableRuntime_IsNullNotAFailure(string? runtime)
    {
        CinemetaMetaDto meta = ValidMeta();
        meta.Runtime = runtime;

        Result<MovieMetadata> result = CinemetaMetadataMapper.MapMovie(meta);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Runtime);
    }

    [Theory]
    [InlineData("2026-10-02T00:00:00.000Z", 2026, 10, 2, 0, 0)]
    [InlineData("2026-10-02", 2026, 10, 2, 0, 0)]
    [InlineData("2026-10-02T03:30:00+03:00", 2026, 10, 2, 0, 30)]
    public void MapMovie_ReleaseTimestamp_IsMappedToUtc(string released, int year, int month, int day, int hour, int minute)
    {
        CinemetaMetaDto meta = ValidMeta();
        meta.Released = released;

        Result<MovieMetadata> result = CinemetaMetadataMapper.MapMovie(meta);

        Assert.Equal(new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc), result.Value.ReleaseAtUtc);
        Assert.Equal(DateTimeKind.Utc, result.Value.ReleaseAtUtc!.Value.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("soon")]
    public void MapMovie_MissingOrInvalidReleaseDate_StaysNull_NeverInvented(string? released)
    {
        CinemetaMetaDto meta = ValidMeta();
        meta.Released = released;

        Result<MovieMetadata> result = CinemetaMetadataMapper.MapMovie(meta);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.ReleaseAtUtc);
    }

    [Fact]
    public void MapMovie_UpcomingMovie_KeepsItsFutureReleaseDate()
    {
        CinemetaMetaDto meta = LoadFixtureMeta("movie-upcoming.json");

        Result<MovieMetadata> result = CinemetaMetadataMapper.MapMovie(meta);

        Assert.True(result.IsSuccess);
        Assert.Equal("Upcoming Feature", result.Value.Title);
        Assert.Equal(new DateTime(2027, 3, 5, 0, 0, 0, DateTimeKind.Utc), result.Value.ReleaseAtUtc);
        Assert.Equal(TimeSpan.FromMinutes(101), result.Value.Runtime);
    }

    [Fact]
    public void MapMovie_FixtureWithoutReleaseDate_LeavesReleaseAtUtcNull()
    {
        CinemetaMetaDto meta = LoadFixtureMeta("movie-missing-release-date.json");

        Result<MovieMetadata> result = CinemetaMetadataMapper.MapMovie(meta);

        Assert.True(result.IsSuccess);
        Assert.Equal(2027, result.Value.Year);
        Assert.Null(result.Value.ReleaseAtUtc);
    }

    [Fact]
    public void MapMovie_IdOnlyStub_IsStillRejectedByTheMapper()
    {
        // The mapper stays strict: a meta object without a name can never become a
        // Movie. Turning Cinemeta's recognized unknown-media stub into MovieNotFound is
        // the provider's job (see CinemetaUnknownMediaTests / CinemetaMovieProviderTests) -
        // it intercepts the stub *before* mapping, so this is only reached by callers
        // that bypass that check.
        CinemetaMetaDto meta = LoadFixtureMeta("movie-id-only-stub.json");

        Result<MovieMetadata> result = CinemetaMetadataMapper.MapMovie(meta);

        Assert.True(result.IsFailure);
        Assert.Equal("Metadata.InvalidResponse", result.Error.Code);
    }
}
