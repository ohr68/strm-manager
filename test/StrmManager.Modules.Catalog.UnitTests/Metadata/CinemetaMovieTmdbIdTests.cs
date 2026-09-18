using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Metadata;
using StrmManager.Modules.Catalog.Infrastructure.Metadata.Cinemeta;

namespace StrmManager.Modules.Catalog.UnitTests.Metadata;

/// <summary>
/// Cinemeta returns the movie's TMDB id as "moviedb_id". It is mapped into
/// MovieMetadata.ExternalIds.TmdbId (a field that already existed and is already persisted -
/// no schema change). It is optional everywhere: a missing, malformed or absurd value is a
/// null TmdbId, never a failure to add the movie.
/// </summary>
public class CinemetaMovieTmdbIdTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private static string MetaJson(string? extraJson) =>
        $$$"""
        {"meta":{"id":"tt0110357","imdb_id":"tt0110357","type":"movie","name":"The Lion King",
         "year":"1994","releaseInfo":"1994","released":"1994-06-24T00:00:00.000Z","runtime":"89 min"{{{(extraJson is null ? "" : "," + extraJson)}}} }}
        """;

    private static Result<MovieMetadata> Map(string? extraJson)
    {
        CinemetaMetaResponseDto response = JsonSerializer.Deserialize<CinemetaMetaResponseDto>(MetaJson(extraJson), SerializerOptions)!;
        return CinemetaMetadataMapper.MapMovie(response.Meta!);
    }

    [Theory]
    [InlineData("\"moviedb_id\":8587", "8587")]
    [InlineData("\"moviedb_id\":420818", "420818")]
    [InlineData("\"moviedb_id\":\"8587\"", "8587")] // tolerated as a numeric string too
    public void MapMovie_MoviedbId_BecomesTheTmdbId(string extra, string expected)
    {
        Result<MovieMetadata> result = Map(extra);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.ExternalIds.TmdbId);
        Assert.Equal("tt0110357", result.Value.ExternalIds.ImdbId); // the IMDb id is untouched
        Assert.Null(result.Value.ExternalIds.TvdbId);
    }

    [Fact]
    public void MapMovie_MissingMoviedbId_IsNullAndStillValid()
    {
        Result<MovieMetadata> result = Map(extraJson: null);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.ExternalIds.TmdbId);
    }

    [Theory]
    [InlineData("\"moviedb_id\":null")]
    [InlineData("\"moviedb_id\":0")]
    [InlineData("\"moviedb_id\":-5")]
    [InlineData("\"moviedb_id\":\"\"")]
    [InlineData("\"moviedb_id\":\"abc\"")]
    [InlineData("\"moviedb_id\":\"12x\"")]
    [InlineData("\"moviedb_id\":\"١٢٣\"")] // non-ASCII digits are not a TMDB id
    [InlineData("\"moviedb_id\":8587.5")]
    [InlineData("\"moviedb_id\":true")]
    [InlineData("\"moviedb_id\":{\"id\":8587}")]
    [InlineData("\"moviedb_id\":[8587]")]
    [InlineData("\"moviedb_id\":99999999999999999999")]
    public void MapMovie_MalformedMoviedbId_IsIgnoredNotAFailure(string extra)
    {
        Result<MovieMetadata> result = Map(extra);

        Assert.True(result.IsSuccess); // a bad optional id must never stop a movie being added
        Assert.Null(result.Value.ExternalIds.TmdbId);
    }

    [Fact]
    public async Task GetMovieAsync_WireResponseWithMoviedbId_ReturnsTheTmdbId()
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(MetaJson("\"moviedb_id\":8587,\"slug\":\"movie/the-lion-king\",\"popularity\":12.5")),
        }));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://cinemeta.example.invalid/") };
        var provider = new CinemetaMetadataProvider(httpClient, NullLogger<CinemetaMetadataProvider>.Instance);

        Result<MovieMetadata> result = await provider.GetMovieAsync("tt0110357");

        Assert.True(result.IsSuccess);
        Assert.Equal("8587", result.Value.ExternalIds.TmdbId);
    }

    [Fact]
    public void UnknownMovieStub_IsStillRecognised_AndOneCarryingMoviedbIdIsNot()
    {
        // moviedb_id stays an "extra" property for CinemetaUnknownMedia, so it can never be
        // mistaken for the observed id/type/behaviorHints-only stub.
        const string stub = """{"meta":{"id":"tt0000000","type":"movie","behaviorHints":{"hasScheduledVideos":false}}}""";
        const string stubWithTmdb = """{"meta":{"id":"tt0000000","type":"movie","moviedb_id":1,"behaviorHints":{"hasScheduledVideos":false}}}""";

        Assert.True(CinemetaUnknownMedia.IsUnknownMovie(JsonSerializer.Deserialize<CinemetaMetaResponseDto>(stub, SerializerOptions)!));
        Assert.False(CinemetaUnknownMedia.IsUnknownMovie(JsonSerializer.Deserialize<CinemetaMetaResponseDto>(stubWithTmdb, SerializerOptions)!));
    }
}
