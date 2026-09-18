using System.Text.Json;
using StrmManager.Modules.Catalog.Infrastructure.Metadata.Cinemeta;

namespace StrmManager.Modules.Catalog.UnitTests.Metadata;

/// <summary>
/// Pins down exactly which HTTP-200 Cinemeta responses count as "this id does not exist",
/// per media type, using ONLY shapes observed live:
///   series -> `{}`
///   movie  -> meta with exactly id, type ("movie") and behaviorHints
/// A shape observed for one type is deliberately NOT assumed for the other, and anything
/// unrecognized stays a malformed response (InvalidResponse), never a guessed NotFound.
/// </summary>
public class CinemetaUnknownMediaTests
{
    private const string ObservedUnknownMovieStub =
        """{"meta":{"id":"tt0000000","type":"movie","behaviorHints":{"hasScheduledVideos":false}}}""";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private static CinemetaMetaResponseDto Parse(string json) =>
        JsonSerializer.Deserialize<CinemetaMetaResponseDto>(json, SerializerOptions)!;

    private static string LoadFixture(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Metadata", "Fixtures", fileName));

    // --- Series ---

    [Fact]
    public void Series_ObservedEmptyObject_IsUnknownSeries()
    {
        Assert.True(CinemetaUnknownMedia.IsUnknownSeries(Parse("{}")));
    }

    [Fact]
    public void Series_HypotheticalIdOnlyStub_IsNotUnknownSeries()
    {
        // The movie-style stub is not assumed to also exist for series.
        Assert.False(CinemetaUnknownMedia.IsUnknownSeries(Parse(ObservedUnknownMovieStub)));
        Assert.False(CinemetaUnknownMedia.IsUnknownSeries(
            Parse("""{"meta":{"id":"tt0000000","type":"series","behaviorHints":{"hasScheduledVideos":false}}}""")));
    }

    [Theory]
    [InlineData("""{"meta":null}""")]
    [InlineData("""{"meta":{}}""")]
    [InlineData("""{"error":"unexpected upstream payload"}""")]
    [InlineData("""{"unexpected":1}""")]
    [InlineData("""{"meta":{"id":"tt1","type":"series","releaseInfo":"2020-"}}""")]
    public void Series_AnythingElse_IsNotUnknownSeries(string json)
    {
        Assert.False(CinemetaUnknownMedia.IsUnknownSeries(Parse(json)));
    }

    [Fact]
    public void Series_RealPayload_IsNotUnknownSeries()
    {
        Assert.False(CinemetaUnknownMedia.IsUnknownSeries(Parse(LoadFixture("stuart-continuing.json"))));
    }

    // --- Movie ---

    [Fact]
    public void Movie_ObservedIdOnlyStub_IsUnknownMovie()
    {
        Assert.True(CinemetaUnknownMedia.IsUnknownMovie(Parse(ObservedUnknownMovieStub)));
        Assert.True(CinemetaUnknownMedia.IsUnknownMovie(Parse(LoadFixture("movie-id-only-stub.json"))));
    }

    [Fact]
    public void Movie_HypotheticalEmptyObject_IsNotUnknownMovie()
    {
        // The series-style `{}` is not assumed to also exist for movies.
        Assert.False(CinemetaUnknownMedia.IsUnknownMovie(Parse("{}")));
    }

    [Theory]
    [InlineData("""{"meta":null}""")]
    [InlineData("""{"meta":{}}""")]
    [InlineData("""{"error":"unexpected upstream payload"}""")]
    [InlineData("""{"meta":{"id":"tt1"}}""")] // id only - not the observed id/type/behaviorHints shape
    [InlineData("""{"meta":{"id":"tt1","type":"movie"}}""")] // behaviorHints missing
    [InlineData("""{"meta":{"id":"tt1","behaviorHints":{}}}""")] // type missing
    [InlineData("""{"meta":{"id":"tt1","type":"series","behaviorHints":{}}}""")] // wrong type for a movie lookup
    [InlineData("""{"meta":{"id":"tt1","type":123,"behaviorHints":{}}}""")] // type is not a string
    [InlineData("""{"meta":{"type":"movie","behaviorHints":{}}}""")] // no id
    [InlineData("""{"meta":{"id":"tt1","type":"movie","behaviorHints":{},"poster":"https://example.invalid/p.jpg"}}""")] // extra data
    [InlineData("""{"meta":{"id":"tt1","type":"movie","behaviorHints":{},"unexpectedField":1}}""")]
    [InlineData("""{"meta":{"id":"tt1","type":"movie","behaviorHints":{}},"extraTopLevel":true}""")] // extra top-level property
    [InlineData("""{"meta":{"id":"tt1","type":"movie","behaviorHints":{},"releaseInfo":"2020"}}""")] // real media object missing its name
    [InlineData("""{"meta":{"id":"tt1","type":"movie","behaviorHints":{},"runtime":"90 min"}}""")]
    [InlineData("""{"meta":{"id":"tt1","type":"movie","behaviorHints":{},"released":"2020-01-01T00:00:00.000Z"}}""")]
    [InlineData("""{"meta":{"id":"tt1","type":"movie","behaviorHints":{},"name":"A Movie"}}""")]
    public void Movie_AnythingElse_IsNotUnknownMovie(string json)
    {
        Assert.False(CinemetaUnknownMedia.IsUnknownMovie(Parse(json)));
    }

    [Fact]
    public void Movie_RealPayload_IsNotUnknownMovie()
    {
        Assert.False(CinemetaUnknownMedia.IsUnknownMovie(Parse(LoadFixture("movie-released.json"))));
    }
}
