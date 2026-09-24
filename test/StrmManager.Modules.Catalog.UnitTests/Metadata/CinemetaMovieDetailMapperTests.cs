using System.Text.Json;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Metadata;
using StrmManager.Modules.Catalog.Infrastructure.Metadata.Cinemeta;

namespace StrmManager.Modules.Catalog.UnitTests.Metadata;

/// <summary>
/// Focused unit tests for CinemetaMetadataMapper.MapMovieDetail (UI-6b) - a separate sibling of MapMovie. Reuses
/// MapMovie's own id/name/year guard-clause tests by construction (same helpers), so only the NEW display fields
/// and the mapper's own success path are covered here; the full malformed-input matrix is not reproduced since it
/// is already proven for the shared guard clauses by CinemetaMovieMetadataMapperTests.
/// </summary>
public class CinemetaMovieDetailMapperTests
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
        Runtime = "100 min",
    };

    [Fact]
    public void MapMovieDetail_ValidCinemetaMovie_MapsAllDisplayFields()
    {
        CinemetaMetaDto meta = LoadFixtureMeta("movie-detail-dark-knight.json");

        Result<MovieDetail> result = CinemetaMetadataMapper.MapMovieDetail(meta);

        Assert.True(result.IsSuccess);
        MovieDetail detail = result.Value;
        Assert.Equal("tt0468569", detail.ExternalId);
        Assert.Equal("The Dark Knight", detail.Title);
        Assert.Equal(2008, detail.Year);
        Assert.Equal(TimeSpan.FromMinutes(152), detail.Runtime);
        Assert.Equal(
            "When a menace known as the Joker wreaks havoc and chaos on the people of Gotham, Batman, James Gordon and Harvey Dent must work together to put an end to the madness.",
            detail.Description);
        Assert.Equal(["Action", "Crime", "Drama"], detail.Genres);
        Assert.Equal("9.1", detail.ImdbRating);
        Assert.Equal("https://images.metahub.space/poster/small/tt0468569/img", detail.PosterUrl);
        Assert.Equal("https://images.metahub.space/background/medium/tt0468569/img", detail.BackdropUrl);
    }

    [Fact]
    public void MapMovieDetail_MissingDisplayFields_AreNullOrEmpty_NeverAFailure()
    {
        CinemetaMetaDto meta = ValidMeta();

        Result<MovieDetail> result = CinemetaMetadataMapper.MapMovieDetail(meta);

        Assert.True(result.IsSuccess);
        MovieDetail detail = result.Value;
        Assert.Null(detail.Description);
        Assert.Empty(detail.Genres);
        Assert.Null(detail.ImdbRating);
        Assert.Null(detail.PosterUrl);
        Assert.Null(detail.BackdropUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MapMovieDetail_MissingOrBlankTitle_ReturnsInvalidResponse(string? name)
    {
        CinemetaMetaDto meta = ValidMeta();
        meta.Name = name;

        Result<MovieDetail> result = CinemetaMetadataMapper.MapMovieDetail(meta);

        Assert.True(result.IsFailure);
        Assert.Equal("Metadata.InvalidResponse", result.Error.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-year")]
    public void MapMovieDetail_MissingOrInvalidYear_ReturnsInvalidResponse(string? releaseInfo)
    {
        CinemetaMetaDto meta = ValidMeta();
        meta.ReleaseInfo = releaseInfo;

        Result<MovieDetail> result = CinemetaMetadataMapper.MapMovieDetail(meta);

        Assert.True(result.IsFailure);
        Assert.Equal("Metadata.InvalidResponse", result.Error.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void MapMovieDetail_MissingProviderId_ReturnsInvalidResponse(string? id)
    {
        CinemetaMetaDto meta = ValidMeta();
        meta.Id = id;

        Result<MovieDetail> result = CinemetaMetadataMapper.MapMovieDetail(meta);

        Assert.True(result.IsFailure);
        Assert.Equal("Metadata.InvalidResponse", result.Error.Code);
    }

    [Fact]
    public void MapMovieDetail_Runtime_ReusesTheSharedMinutesParser()
    {
        CinemetaMetaDto meta = ValidMeta();
        meta.Runtime = "95min";

        Result<MovieDetail> result = CinemetaMetadataMapper.MapMovieDetail(meta);

        Assert.Equal(TimeSpan.FromMinutes(95), result.Value.Runtime);
    }
}
