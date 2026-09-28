using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using StrmManager.Modules.Catalog.Application.Playback;
using StrmManager.Modules.Catalog.IntegrationTests.Infrastructure;

namespace StrmManager.Modules.Catalog.IntegrationTests.Playback;

public class StreamEpisodeEndpointTests(
    ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly Guid Episode =
        Guid.Parse("93f3eaa7-7804-44a5-a7c1-b73c7677d408");

    public static TheoryData<string> Methods =>
        new() { "GET", "HEAD" };

    private StablePlaybackHost NewHost() =>
        new(factory);

    private static PlaybackCoordinationResult.Completed Resolved(
        string url) =>
        new(
            new PlaybackResolutionResult.Resolved(
                "FrostStream",
                "Episode Source",
                new PlaybackLocation(url)));

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task Resolved_Redirects307_WithExactLocation_NoStore_AndNoBody(
        string method)
    {
        var host = NewHost();

        host.EpisodeCoordinator.Returns(
            Resolved(Canary.Url));

        ObservedResponse response =
            await host.SendAsync(
                method,
                StablePlaybackHost.EpisodeRouteFor(Episode));

        Assert.Equal(307, response.Status);
        Assert.Equal(Canary.Url, response.Location);
        Assert.Equal(
            "no-store",
            response.Header("Cache-Control"));
        Assert.Equal(
            "0",
            response.Header("Content-Length"));
        Assert.Empty(response.Body);

        Assert.Equal(
            Episode,
            Assert.Single(
                host.EpisodeCoordinator.Calls).EpisodeId);

        Assert.Empty(host.Coordinator.Calls);
    }

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task NotFound_Returns404_WithoutLocation(
        string method)
    {
        var host = NewHost();

        host.EpisodeCoordinator.Returns(
            new PlaybackCoordinationResult.Completed(
                new PlaybackResolutionResult.NotFound()));

        ObservedResponse response =
            await host.SendAsync(
                method,
                StablePlaybackHost.EpisodeRouteFor(Episode));

        Assert.Equal(404, response.Status);
        Assert.Null(response.Location);
        Assert.Equal(
            "no-store",
            response.Header("Cache-Control"));
        Assert.Empty(response.Body);
    }

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task UnavailableAndCoordinationFailure_Return503(
        string method)
    {
        PlaybackCoordinationResult[] outcomes =
        [
            new PlaybackCoordinationResult.Completed(
                new PlaybackResolutionResult.Unavailable(
                    PlaybackUnavailableReason.ProviderFailure)),

            new PlaybackCoordinationResult.Failed(
                PlaybackCoordinationFailure.BudgetExceeded),

            new PlaybackCoordinationResult.Failed(
                PlaybackCoordinationFailure.ShuttingDown),

            new PlaybackCoordinationResult.Failed(
                PlaybackCoordinationFailure.Faulted),
        ];

        foreach (PlaybackCoordinationResult outcome in outcomes)
        {
            var host = NewHost();
            host.EpisodeCoordinator.Returns(outcome);

            ObservedResponse response =
                await host.SendAsync(
                    method,
                    StablePlaybackHost.EpisodeRouteFor(Episode));

            Assert.Equal(503, response.Status);
            Assert.Null(response.Location);
            Assert.Equal(
                "no-store",
                response.Header("Cache-Control"));
            Assert.Empty(response.Body);
        }
    }

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task Busy_Returns429_WithFixedRetryAfter(
        string method)
    {
        var host = NewHost();

        host.EpisodeCoordinator.Returns(
            new PlaybackCoordinationResult.Busy());

        ObservedResponse response =
            await host.SendAsync(
                method,
                StablePlaybackHost.EpisodeRouteFor(Episode));

        Assert.Equal(429, response.Status);
        Assert.Equal(
            "5",
            response.Header("Retry-After"));
        Assert.Null(response.Location);
        Assert.Equal(
            "no-store",
            response.Header("Cache-Control"));
        Assert.Empty(response.Body);
    }

    [Fact]
    public async Task UnsafeProviderUrl_FailsClosedAs503_AndIsNotEmitted()
    {
        var host = NewHost();

        string unsafeUrl =
            $"{Canary.Url}\r\nSet-Cookie: injected={Canary.QuerySecret}";

        host.EpisodeCoordinator.Returns(
            Resolved(unsafeUrl));

        ObservedResponse response =
            await host.SendAsync(
                "GET",
                StablePlaybackHost.EpisodeRouteFor(Episode));

        Assert.Equal(503, response.Status);
        Assert.Null(response.Location);

        foreach (string part in Canary.Parts)
        {
            Assert.DoesNotContain(
                part,
                response.EverythingButLocation,
                StringComparison.OrdinalIgnoreCase);

            Assert.DoesNotContain(
                part,
                host.Logs.AllText,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task ProviderUrl_AppearsOnlyInLocation(
        string method)
    {
        var host = NewHost();

        host.EpisodeCoordinator.Returns(
            Resolved(Canary.Url));

        ObservedResponse response =
            await host.SendAsync(
                method,
                StablePlaybackHost.EpisodeRouteFor(Episode));

        Assert.Equal(
            Canary.Url,
            response.Location);

        foreach (string part in Canary.Parts)
        {
            Assert.DoesNotContain(
                part,
                response.EverythingButLocation,
                StringComparison.OrdinalIgnoreCase);

            Assert.DoesNotContain(
                part,
                host.Logs.AllText,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task OtherMethods_Return405_AndNeverReachCoordinator(
        string method)
    {
        var host = NewHost();

        host.EpisodeCoordinator.Returns(
            Resolved(Canary.Url));

        ObservedResponse response =
            await host.SendAsync(
                method,
                StablePlaybackHost.EpisodeRouteFor(Episode));

        Assert.Equal(405, response.Status);
        Assert.Contains(
            "GET",
            response.Header("Allow") ?? string.Empty);
        Assert.Contains(
            "HEAD",
            response.Header("Allow") ?? string.Empty);

        Assert.Empty(
            host.EpisodeCoordinator.Calls);
    }

    [Fact]
    public async Task RouteUsesEpisodeGuid_AndRequiresNoAuthorization()
    {
        var host = NewHost();

        host.EpisodeCoordinator.Returns(
            Resolved(Canary.Url));

        ObservedResponse response =
            await host.SendAsync(
                "GET",
                StablePlaybackHost.EpisodeRouteFor(Episode));

        Assert.Equal(307, response.Status);

        RouteEndpoint stream =
            Assert.Single(host.Services
                    .GetRequiredService<EndpointDataSource>()
                    .Endpoints
                    .OfType<RouteEndpoint>()
, endpoint =>
                            (endpoint.RoutePattern.RawText ?? string.Empty)
                            .TrimStart('/')
                            .Equals(
                                "media/episodes/{episodeId:guid}/stream",
                                StringComparison.Ordinal));

        Assert.Empty(
            stream.Metadata.GetOrderedMetadata<IAuthorizeData>());

        Assert.Equal(
            ["GET", "HEAD"],
            stream.Metadata
                .GetMetadata<HttpMethodMetadata>()!
                .HttpMethods
                .Order());
    }

    [Theory]
    [InlineData("/media/episodes/not-a-guid/stream")]
    [InlineData("/media/episodes/12345/stream")]
    [InlineData("/media/episodes/S02E03/stream")]
    [InlineData("/media/episodes/93f3eaa7-7804-44a5-a7c1-b73c7677d408")]
    [InlineData("/media/episodes/93f3eaa7-7804-44a5-a7c1-b73c7677d408/stream/extra")]
    public async Task InvalidEpisodeRoute_Returns404_WithoutCallingCoordinator(
        string path)
    {
        var host = NewHost();

        host.EpisodeCoordinator.Returns(
            Resolved(Canary.Url));

        ObservedResponse response =
            await host.SendAsync(
                "GET",
                path);

        Assert.Equal(404, response.Status);
        Assert.Null(response.Location);
        Assert.Empty(
            host.EpisodeCoordinator.Calls);
    }
}
