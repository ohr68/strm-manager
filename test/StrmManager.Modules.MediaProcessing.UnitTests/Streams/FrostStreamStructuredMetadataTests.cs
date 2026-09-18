using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.MediaProcessing.Application.Streams;
using StrmManager.Modules.MediaProcessing.Infrastructure.Streams.FrostStream;

namespace StrmManager.Modules.MediaProcessing.UnitTests.Streams;

/// <summary>
/// The only structured facts kept from a FrostStream stream entry beyond name/title/url:
/// a TMDB id, but only from the one observed structured form (behaviorHints.bingeGroup =
/// "megaembed-movie-{number}"), and whether custom request headers are required. Cloutstream
/// bingeGroups are localized-title slugs - never identity - and unknown formats mean nothing.
/// Header VALUES (and the URL) are never carried onto the candidate.
/// </summary>
public class FrostStreamStructuredMetadataTests
{
    private const string SecretUserAgent = "UA-SECRET-VALUE";
    private const string SecretReferer = "https://referer-secret.example.test/";

    private static readonly MovieStreamReference Reference = new("tt0110357", "The Lion King", 1994, TimeSpan.FromMinutes(89));

    private static async Task<IReadOnlyList<StreamCandidate>> LookUpAsync(string streamsJson)
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($$"""{ "streams": {{streamsJson}} }"""),
        }));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://froststream.example.invalid/") };
        var provider = new FrostStreamProvider(httpClient, NullLogger<FrostStreamProvider>.Instance);

        Result<IReadOnlyList<StreamCandidate>> result = await provider.GetMovieStreamsAsync(Reference);

        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static string Stream(string? bingeGroupJson = null, string? headersJson = null, string? behaviorHintsJson = null)
    {
        string behaviorHints = behaviorHintsJson
            ?? (bingeGroupJson is null ? "" : $$""", "behaviorHints": { "notWebReady": true, "bingeGroup": {{bingeGroupJson}} }""");
        string headers = headersJson is null ? "" : $$""", "headers": {{headersJson}}""";

        return $$"""[ { "name": "FrostStream 720p", "title": "🎬 Titulo\n🍃 MegaEmbed\n🌎 Português", "url": "https://media.example.test/video?token=URL-SECRET"{{headers}}{{behaviorHints}} } ]""";
    }

    // --- TMDB id from the supported structured form only ---

    [Fact]
    public async Task MegaEmbedBingeGroup_ExposesTheNumericPartAsTheCandidateTmdbId()
    {
        StreamCandidate candidate = Assert.Single(await LookUpAsync(Stream("\"megaembed-movie-420818\"")));

        Assert.Equal("420818", candidate.TmdbId);
    }

    [Theory]
    [InlineData("cloutstream-cs:movie:o-rei-leao")] // a localized-title slug is not identity
    [InlineData("cloutstream-cs:movie:o-rei-leao-1994")] // ...even with a year suffix
    [InlineData("megaembed-movie-")]
    [InlineData("megaembed-movie-abc")]
    [InlineData("megaembed-movie-807x")]
    [InlineData("megaembed-movie-807-extra")]
    [InlineData("megaembed-movie--807")]
    [InlineData("megaembed-movie-1234567890123")] // absurdly long: not a TMDB id
    [InlineData("megaembed-series-807")]
    [InlineData("MEGAEMBED-MOVIE-807")] // only the exact observed form is understood
    [InlineData("prefix-megaembed-movie-807")]
    [InlineData("other-provider-807")]
    [InlineData("")]
    public async Task AnyOtherBingeGroup_IsIgnored(string bingeGroup)
    {
        StreamCandidate candidate = Assert.Single(await LookUpAsync(Stream($"\"{bingeGroup}\"")));

        Assert.Null(candidate.TmdbId);
    }

    [Theory]
    [InlineData("5")] // a number, not a string
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("""{ "nested": "megaembed-movie-807" }""")]
    [InlineData("""[ "megaembed-movie-807" ]""")]
    public async Task ABingeGroupOfTheWrongJsonType_IsIgnoredAndDoesNotFailTheLookup(string bingeGroupJson)
    {
        StreamCandidate candidate = Assert.Single(await LookUpAsync(Stream(bingeGroupJson)));

        Assert.Null(candidate.TmdbId);
    }

    [Theory]
    [InlineData("\"not an object\"")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    public async Task BehaviorHintsOfTheWrongJsonType_AreIgnoredAndDoNotFailTheLookup(string behaviorHintsValue)
    {
        StreamCandidate candidate = Assert.Single(await LookUpAsync(Stream(behaviorHintsJson: $$""", "behaviorHints": {{behaviorHintsValue}}""")));

        Assert.Null(candidate.TmdbId);
        Assert.False(candidate.RequiresCustomHeaders);
    }

    [Fact]
    public async Task NoBehaviorHints_MeansNoTmdbId()
    {
        StreamCandidate candidate = Assert.Single(await LookUpAsync(Stream()));

        Assert.Null(candidate.TmdbId);
    }

    // --- Custom headers ---

    [Fact]
    public async Task ANonEmptyHeadersObject_MeansCustomHeadersAreRequired()
    {
        StreamCandidate candidate = Assert.Single(await LookUpAsync(Stream(
            headersJson: $$"""{ "User-Agent": "{{SecretUserAgent}}", "Referer": "{{SecretReferer}}", "Origin": "https://origin.example.test" }""")));

        Assert.True(candidate.RequiresCustomHeaders);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[]")]
    public async Task AnAbsentEmptyOrNullHeadersValue_MeansNoCustomHeaders(string headersJson)
    {
        Assert.False(Assert.Single(await LookUpAsync(Stream(headersJson: headersJson))).RequiresCustomHeaders);
        Assert.False(Assert.Single(await LookUpAsync(Stream())).RequiresCustomHeaders);
    }

    [Theory]
    [InlineData("\"a string\"")]
    [InlineData("[ \"x\" ]")]
    [InlineData("1")]
    public async Task AnUnrecognisedNonEmptyHeadersValue_IsTreatedAsRequiringHeaders_TheSafeReading(string headersJson)
    {
        Assert.True(Assert.Single(await LookUpAsync(Stream(headersJson: headersJson))).RequiresCustomHeaders);
    }

    [Fact]
    public async Task HeaderValuesAndTheUrl_NeverAppearInAnythingTheCandidateExposes()
    {
        StreamCandidate candidate = Assert.Single(await LookUpAsync(Stream(
            "\"megaembed-movie-420818\"",
            $$"""{ "User-Agent": "{{SecretUserAgent}}", "Referer": "{{SecretReferer}}" }""")));

        // Every public property value, and the record's own text form (what an accidental
        // log line or a failing assertion would print), is free of header values and the URL.
        var exposed = new List<string> { candidate.ToString() };
        exposed.AddRange(typeof(StreamCandidate).GetProperties()
            .Where(p => p.Name != nameof(StreamCandidate.Url))
            .Select(p => p.GetValue(candidate)?.ToString() ?? string.Empty));

        Assert.All(exposed, text =>
        {
            Assert.DoesNotContain(SecretUserAgent, text, StringComparison.Ordinal);
            Assert.DoesNotContain("referer-secret", text, StringComparison.Ordinal);
            Assert.DoesNotContain("URL-SECRET", text, StringComparison.Ordinal);
            Assert.DoesNotContain("media.example.test", text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task ExistingFields_AreUnchanged()
    {
        StreamCandidate candidate = Assert.Single(await LookUpAsync(Stream("\"megaembed-movie-420818\"")));

        Assert.Equal("FrostStream", candidate.Provider);
        Assert.Equal("FrostStream 720p", candidate.Name);
        Assert.Equal("🎬 Titulo\n🍃 MegaEmbed\n🌎 Português", candidate.Description);
        Assert.Equal("https://media.example.test/video?token=URL-SECRET", candidate.Url); // still what the STRM will contain
    }
}
