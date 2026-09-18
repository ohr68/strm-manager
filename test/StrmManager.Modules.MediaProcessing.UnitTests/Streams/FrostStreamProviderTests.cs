using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.MediaProcessing.Application.Streams;
using StrmManager.Modules.MediaProcessing.Infrastructure.Streams.FrostStream;

namespace StrmManager.Modules.MediaProcessing.UnitTests.Streams;

/// <summary>
/// "Wrong episode identity"/"mixed conflicting references" from the FrostStream test
/// matrix are covered by EpisodeIdentityValidatorTests - the provider itself does no
/// identity filtering, it just maps whatever candidates FrostStream returned (identity
/// validation is the orchestrator's job, using EpisodeIdentityValidator on each
/// candidate). Never depends on live FrostStream - a fake HttpMessageHandler stands in.
/// </summary>
public class FrostStreamProviderTests
{
    private static readonly EpisodeStreamReference Reference = new("tt27497393:1:8", 1, 8, "Episode Eight", TimeSpan.FromMinutes(24));

    private static FrostStreamProvider CreateProvider(FakeHttpMessageHandler handler, TimeSpan? timeout = null)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://froststream.example.invalid/"),
            Timeout = timeout ?? TimeSpan.FromSeconds(30),
        };

        return new FrostStreamProvider(httpClient, NullLogger<FrostStreamProvider>.Instance);
    }

    [Fact]
    public async Task GetEpisodeStreamsAsync_EmptyStreamsArray_ReturnsSuccessWithNoCandidates()
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{ "streams": [] }"""),
        }));

        Result<IReadOnlyList<StreamCandidate>> result = await CreateProvider(handler).GetEpisodeStreamsAsync(Reference);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task GetEpisodeStreamsAsync_OneCandidate_ReturnsMappedCandidate()
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{ "streams": [ { "name": "FrostStream 1080p", "title": "S01E08", "url": "https://media.example.test/video" } ] }"""),
        }));

        Result<IReadOnlyList<StreamCandidate>> result = await CreateProvider(handler).GetEpisodeStreamsAsync(Reference);

        Assert.True(result.IsSuccess);
        StreamCandidate candidate = Assert.Single(result.Value);
        Assert.Equal("FrostStream", candidate.Provider);
        Assert.Equal("FrostStream 1080p", candidate.Name);
        Assert.Equal("https://media.example.test/video", candidate.Url);
    }

    [Fact]
    public async Task GetEpisodeStreamsAsync_MultipleCandidates_ReturnsAllInResponseOrder()
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {
                    "streams": [
                        { "name": "Source A", "title": "S01E08 720p", "url": "https://media.example.test/a" },
                        { "name": "Source B", "title": "S01E08 1080p", "url": "https://media.example.test/b" }
                    ]
                }
                """),
        }));

        Result<IReadOnlyList<StreamCandidate>> result = await CreateProvider(handler).GetEpisodeStreamsAsync(Reference);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal("Source A", result.Value[0].Name);
        Assert.Equal("Source B", result.Value[1].Name);
    }

    [Fact]
    public async Task GetEpisodeStreamsAsync_NonHttpUrl_IsFilteredOut()
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{ "streams": [ { "name": "Bad", "url": "file:///etc/passwd" } ] }"""),
        }));

        Result<IReadOnlyList<StreamCandidate>> result = await CreateProvider(handler).GetEpisodeStreamsAsync(Reference);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task GetEpisodeStreamsAsync_ServerError_ReturnsProviderUnavailable()
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)));

        Result<IReadOnlyList<StreamCandidate>> result = await CreateProvider(handler).GetEpisodeStreamsAsync(Reference);

        Assert.True(result.IsFailure);
        Assert.Equal("Streams.ProviderUnavailable", result.Error.Code);
    }

    [Fact]
    public async Task GetEpisodeStreamsAsync_MalformedJson_ReturnsInvalidResponse()
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ not valid json"),
        }));

        Result<IReadOnlyList<StreamCandidate>> result = await CreateProvider(handler).GetEpisodeStreamsAsync(Reference);

        Assert.True(result.IsFailure);
        Assert.Equal("Streams.InvalidResponse", result.Error.Code);
    }

    [Fact]
    public async Task GetEpisodeStreamsAsync_RequestExceedsTimeout_ReturnsTimeout()
    {
        var handler = new FakeHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        Result<IReadOnlyList<StreamCandidate>> result = await CreateProvider(handler, timeout: TimeSpan.FromMilliseconds(50))
            .GetEpisodeStreamsAsync(Reference);

        Assert.True(result.IsFailure);
        Assert.Equal("Streams.Timeout", result.Error.Code);
    }

    [Fact]
    public async Task GetEpisodeStreamsAsync_EpisodeIdWithColons_IsUrlEncoded()
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{ "streams": [] }"""),
        }));

        await CreateProvider(handler).GetEpisodeStreamsAsync(Reference);

        Assert.NotNull(handler.LastRequest);
        string requestedPath = handler.LastRequest!.RequestUri!.AbsoluteUri;
        Assert.Contains("tt27497393%3A1%3A8", requestedPath, StringComparison.Ordinal);
        Assert.DoesNotContain("tt27497393:1:8", requestedPath, StringComparison.Ordinal);
    }
}
