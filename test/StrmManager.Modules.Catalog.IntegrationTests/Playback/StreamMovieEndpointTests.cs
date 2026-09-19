using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using StrmManager.Modules.Catalog.Application.Playback;
using StrmManager.Modules.Catalog.IntegrationTests.Infrastructure;

namespace StrmManager.Modules.Catalog.IntegrationTests.Playback;

/// <summary>
/// GET/HEAD /media/{movieId}/stream as an HTTP contract, with a controllable coordinator behind it (no provider, no ffprobe,
/// no database). Each outcome the coordinator can report is driven in and the exact response is checked: status, headers,
/// body, and - above all - that a provider URL appears in the Location header and nowhere else.
/// </summary>
public class StreamMovieEndpointTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly Guid Movie = Guid.Parse("7c1f0c1e-38a4-4d0e-9d7b-2f6a1e5b9c30");

    public static TheoryData<string> Methods => new() { "GET", "HEAD" };

    public static TheoryData<string, string> EveryNonRedirectOutcome()
    {
        var data = new TheoryData<string, string>();

        foreach (string method in new[] { "GET", "HEAD" })
        {
            foreach (string outcome in new[]
            {
                "notfound", "busy",
                "unavailable-missing-imdb", "unavailable-provider", "unavailable-nocandidates", "unavailable-noapproved",
                "failed-budget", "failed-shutdown", "failed-faulted", "resolved-with-unsendable-url",
            })
            {
                data.Add(method, outcome);
            }
        }

        return data;
    }

    private static PlaybackCoordinationResult Outcome(string outcome) => outcome switch
    {
        "notfound" => new PlaybackCoordinationResult.Completed(new PlaybackResolutionResult.NotFound()),
        "busy" => new PlaybackCoordinationResult.Busy(),
        "unavailable-missing-imdb" => Unavailable(PlaybackUnavailableReason.MissingImdbId),
        "unavailable-provider" => Unavailable(PlaybackUnavailableReason.ProviderFailure),
        "unavailable-nocandidates" => Unavailable(PlaybackUnavailableReason.NoCandidates),
        "unavailable-noapproved" => Unavailable(PlaybackUnavailableReason.NoApprovedCandidate),
        "failed-budget" => new PlaybackCoordinationResult.Failed(PlaybackCoordinationFailure.BudgetExceeded),
        "failed-shutdown" => new PlaybackCoordinationResult.Failed(PlaybackCoordinationFailure.ShuttingDown),
        "failed-faulted" => new PlaybackCoordinationResult.Failed(PlaybackCoordinationFailure.Faulted),

        // A "resolved" source whose URL cannot legally be a header value (a line break): fail closed, and never emit it.
        "resolved-with-unsendable-url" => Resolved($"{Canary.Url}\r\nSet-Cookie: injected={Canary.QuerySecret}"),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };

    private static PlaybackCoordinationResult.Completed Unavailable(PlaybackUnavailableReason reason) =>
        new PlaybackCoordinationResult.Completed(new PlaybackResolutionResult.Unavailable(reason));

    private static PlaybackCoordinationResult.Completed Resolved(string url) =>
        new PlaybackCoordinationResult.Completed(new PlaybackResolutionResult.Resolved("FrostStream", "Source A", new PlaybackLocation(url)));

    private static void AssertNoCanary(string text)
    {
        foreach (string part in Canary.Parts)
        {
            Assert.DoesNotContain(part, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    private StablePlaybackHost NewHost() => new(factory);

    // ================= success: 307 =================

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task Resolved_Redirects307_WithExactLocation_NoStore_AndNoBody(string method)
    {
        var host = NewHost();
        host.Coordinator.Returns(Resolved(Canary.Url));

        ObservedResponse response = await host.SendAsync(method, StablePlaybackHost.RouteFor(Movie));

        Assert.Equal(307, response.Status);
        Assert.Equal(Canary.Url, response.Location); // exactly what the coordinator returned
        Assert.Equal("no-store", response.Header("Cache-Control"));
        Assert.Empty(response.Body);
        Assert.Equal("0", response.Header("Content-Length"));
    }

    [Fact]
    public async Task GetAndHead_ReturnTheSameStatusAndLocation_ThroughTheSameCoordinator()
    {
        var host = NewHost();
        host.Coordinator.Returns(Resolved(Canary.Url));

        ObservedResponse get = await host.SendAsync("GET", StablePlaybackHost.RouteFor(Movie));
        ObservedResponse head = await host.SendAsync("HEAD", StablePlaybackHost.RouteFor(Movie));

        Assert.Equal(get.Status, head.Status);
        Assert.Equal(get.Location, head.Location);
        Assert.Equal(get.Header("Cache-Control"), head.Header("Cache-Control"));
        Assert.Empty(head.Body);

        // Two requests, two calls to the very same abstraction - each with the movie id from the route.
        Assert.Equal([Movie, Movie], host.Coordinator.Calls.Select(c => c.MovieId));
    }

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task ARangeHeader_IsIgnored_TheSame307IsReturned_AndNoRangeResponseIsSynthesized(string method)
    {
        var host = NewHost();
        host.Coordinator.Returns(Resolved(Canary.Url));

        foreach (string range in new[] { "bytes=0-", "bytes=100-200", "bytes=-500", "bytes=0-0,5-9" })
        {
            ObservedResponse response = await host.SendAsync(method, StablePlaybackHost.RouteFor(Movie), range);

            Assert.Equal(307, response.Status); // never 206 / 416
            Assert.Equal(Canary.Url, response.Location);
            Assert.False(response.HasHeader("Content-Range"));
            Assert.False(response.HasHeader("Accept-Ranges"));
            Assert.Empty(response.Body); // no media bytes
        }

        Assert.Equal(4, host.Coordinator.Calls.Count); // and Range did not change what is resolved
    }

    [Fact]
    public async Task TheCoordinatorReceivesTheRequestToken_SoAClientGoingAwayCancelsOnlyItsOwnWait()
    {
        var host = NewHost();
        host.Coordinator.Returns(Resolved(Canary.Url));

        await host.SendAsync("GET", StablePlaybackHost.RouteFor(Movie));

        (Guid movieId, CancellationToken token) = Assert.Single(host.Coordinator.Calls);
        Assert.Equal(Movie, movieId);
        Assert.True(token.CanBeCanceled); // the request-aborted token, not CancellationToken.None
    }

    // ================= every other outcome: generic, empty, no-store, no Location =================

    [Theory]
    [MemberData(nameof(EveryNonRedirectOutcome))]
    public async Task EveryNonRedirectOutcome_IsGeneric_Empty_NoStore_AndHasNoLocation(string method, string outcome)
    {
        var host = NewHost();
        host.Coordinator.Returns(Outcome(outcome));

        ObservedResponse response = await host.SendAsync(method, StablePlaybackHost.RouteFor(Movie));

        int expected = outcome switch { "notfound" => 404, "busy" => 429, _ => 503 };
        Assert.Equal(expected, response.Status);
        Assert.Empty(response.Body);
        Assert.Equal("no-store", response.Header("Cache-Control"));
        Assert.Null(response.Location);
        Assert.False(response.HasHeader("Content-Type")); // no problem-details or any other body format

        // Nothing about why: no reason enum or provider/validator word reaches the client in any header.
        foreach (string word in new[] { "MissingImdbId", "ProviderFailure", "NoCandidates", "NoApprovedCandidate", "BudgetExceeded", "ShuttingDown", "Faulted", "FrostStream" })
        {
            Assert.DoesNotContain(word, response.EverythingButLocation, StringComparison.OrdinalIgnoreCase);
        }

        AssertNoCanary(response.EverythingButLocation);
        AssertNoCanary(host.Logs.AllText);
    }

    [Fact]
    public async Task Busy_Returns429_WithAFixedRetryAfterOfFiveSeconds_AndOnlyThen()
    {
        var host = NewHost();

        host.Coordinator.Returns(new PlaybackCoordinationResult.Busy());
        ObservedResponse busy = await host.SendAsync("GET", StablePlaybackHost.RouteFor(Movie));

        Assert.Equal(429, busy.Status);
        Assert.Equal("5", busy.Header("Retry-After"));

        foreach (string outcome in new[] { "notfound", "unavailable-provider", "failed-budget", "failed-faulted" })
        {
            host.Coordinator.Returns(Outcome(outcome));
            Assert.False((await host.SendAsync("GET", StablePlaybackHost.RouteFor(Movie))).HasHeader("Retry-After"));
        }

        host.Coordinator.Returns(Resolved(Canary.Url));
        Assert.False((await host.SendAsync("GET", StablePlaybackHost.RouteFor(Movie))).HasHeader("Retry-After"));
    }

    [Fact]
    public async Task ARedirectIsTheOnlyResponseWithALocation()
    {
        var host = NewHost();
        var withLocation = new List<string>();

        foreach (string outcome in new[] { "notfound", "busy", "unavailable-noapproved", "failed-budget", "failed-shutdown", "failed-faulted", "resolved-with-unsendable-url" })
        {
            host.Coordinator.Returns(Outcome(outcome));
            if ((await host.SendAsync("GET", StablePlaybackHost.RouteFor(Movie))).Location is not null)
            {
                withLocation.Add(outcome);
            }
        }

        Assert.Empty(withLocation);
    }

    [Theory]
    [InlineData("a control character", "https://canary-host-b81d47.invalid/ab")]
    [InlineData("a line break", "https://canary-host-b81d47.invalid/a\nb")]
    [InlineData("non-ASCII text", "https://canary-host-b81d47.invalid/café")]
    public async Task AUrlThatCannotBeAHeaderValue_FailsClosedAs503_AndIsNeverEmitted(string _, string url)
    {
        var host = NewHost();
        host.Coordinator.Returns(Resolved(url));

        ObservedResponse response = await host.SendAsync("GET", StablePlaybackHost.RouteFor(Movie));

        Assert.Equal(503, response.Status);
        Assert.Null(response.Location);
        Assert.DoesNotContain("canary-host", response.EverythingButLocation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("canary-host", host.Logs.AllText, StringComparison.OrdinalIgnoreCase);
    }

    // ================= URL confidentiality =================

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task ASuccessfulCanaryUrl_AppearsInTheLocationHeaderAndNowhereElse(string method)
    {
        var host = NewHost();
        host.Coordinator.Returns(Resolved(Canary.Url));

        ObservedResponse response = await host.SendAsync(method, StablePlaybackHost.RouteFor(Movie));

        Assert.Equal(Canary.Url, response.Location); // the one intended place...

        // ...and with Location set aside, every other header and the body are clean.
        AssertNoCanary(response.EverythingButLocation);

        // Every log entry at every level, framework categories included, message + structured properties + exception text.
        Assert.NotEmpty(host.Logs.Entries);
        AssertNoCanary(host.Logs.AllText);

        // What the server saw of the request: its path and recorded outcome carry the movie id only.
        Assert.All(host.Outcomes.Outcomes, o => AssertNoCanary($"{o.Method} {o.Path}"));
        Assert.All(host.Coordinator.Calls, c => AssertNoCanary(c.MovieId.ToString()));
    }

    [Fact]
    public async Task TheLogScanReallySeesFrameworkLogs_SoAFrameworkRedirectHelperCouldNotHideItsDestination()
    {
        // The app's own config keeps Microsoft.AspNetCore at Warning. This host lifts that, so the scan above covers the very
        // category a framework redirect ("Executing RedirectResult, redirecting to {Destination}") would log to.
        var host = NewHost();
        host.Coordinator.Returns(Resolved(Canary.Url));

        await host.SendAsync("GET", StablePlaybackHost.RouteFor(Movie));

        Assert.Contains(host.Logs.Entries, entry => entry.StartsWith("Microsoft.AspNetCore.Hosting.Diagnostics", StringComparison.Ordinal));
        Assert.Contains(host.Logs.Entries, entry => entry.Contains($"/media/{Movie}/stream", StringComparison.Ordinal)); // the request path is logged - it holds only the id
    }

    // ================= routing: methods, ids, access =================

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task OtherMethods_Get405_AndNeverReachTheCoordinator(string method)
    {
        var host = NewHost();
        host.Coordinator.Returns(Resolved(Canary.Url));

        ObservedResponse response = await host.SendAsync(method, StablePlaybackHost.RouteFor(Movie));

        Assert.Equal(405, response.Status);
        Assert.Contains("GET", response.Header("Allow") ?? string.Empty);
        Assert.Contains("HEAD", response.Header("Allow") ?? string.Empty);
        Assert.Empty(host.Coordinator.Calls);
        Assert.Null(response.Location);
    }

    [Theory]
    [InlineData("/media/not-a-guid/stream")]
    [InlineData("/media/tt0111161/stream")]           // an IMDb id is not a route identifier
    [InlineData("/media/12345/stream")]                 // neither is a TMDB id
    [InlineData("/media/The-Shawshank-Redemption/stream")]
    [InlineData("/media/7c1f0c1e-38a4-4d0e-9d7b-2f6a1e5b9c30x/stream")]
    [InlineData("/media/7c1f0c1e-38a4-4d0e-9d7b-2f6a1e5b9c30")]
    [InlineData("/media/7c1f0c1e-38a4-4d0e-9d7b-2f6a1e5b9c30/stream/extra")]
    public async Task ARouteThatIsNotAGuid_IsNotFound_AndNeverReachesTheCoordinator(string path)
    {
        var host = NewHost();
        host.Coordinator.Returns(Resolved(Canary.Url));

        foreach (string method in new[] { "GET", "HEAD" })
        {
            ObservedResponse response = await host.SendAsync(method, path);

            Assert.Equal(404, response.Status);
            Assert.Null(response.Location);
        }

        Assert.Empty(host.Coordinator.Calls);
    }

    [Fact]
    public async Task TheRouteNeedsNoToken_AndCarriesNoAuthorizationRequirement()
    {
        var host = NewHost();
        host.Coordinator.Returns(Resolved(Canary.Url));

        // A bare request: no Authorization header, no cookie, no key - the way Jellyfin/ffmpeg calls it.
        ObservedResponse response = await host.SendAsync("GET", StablePlaybackHost.RouteFor(Movie));
        Assert.Equal(307, response.Status);

        RouteEndpoint[] mediaEndpoints = host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => (e.RoutePattern.RawText ?? string.Empty).TrimStart('/').StartsWith("media", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        RouteEndpoint stream = Assert.Single(mediaEndpoints);
        Assert.Equal("media/{movieId:guid}/stream", stream.RoutePattern.RawText!.TrimStart('/'));
        Assert.Empty(stream.Metadata.GetOrderedMetadata<IAuthorizeData>()); // nothing requires a login or elevation
        Assert.Equal(["GET", "HEAD"], stream.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Order());
    }

    [Fact]
    public void NoOtherRouteHasBeenGivenTheMediaPath_OrAnAnonymousOverride()
    {
        var host = NewHost();

        RouteEndpoint[] all = host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToArray();

        // Existing endpoints are exactly as before: none of them allows anonymous access explicitly, because there is nothing to override.
        Assert.DoesNotContain(all, e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null);
        Assert.Contains(all, e => e.RoutePattern.RawText!.TrimStart('/').StartsWith("api/movies", StringComparison.Ordinal));
    }

    // ================= cancellation =================

    [Fact]
    public async Task ARequestThatIsCancelled_IsNotTurnedInto503()
    {
        var host = NewHost();
        var serverSawCancellation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Coordinator.Handler = async (_, token) =>
        {
            serverReached.SetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, token); // an unfinished resolution this waiter has stopped waiting for
                throw new InvalidOperationException("unreachable");
            }
            catch (OperationCanceledException)
            {
                serverSawCancellation.SetResult();
                throw; // exactly what the real coordinator does for a cancelled waiter
            }
        };

        using var clientGoesAway = new CancellationTokenSource();
        Task<ObservedResponse> request = host.SendAsync("GET", StablePlaybackHost.RouteFor(Movie), cancellationToken: clientGoesAway.Token);
        await serverReached.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await clientGoesAway.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        await serverSawCancellation.Task.WaitAsync(TimeSpan.FromSeconds(10)); // the endpoint's token really was cancelled

        await WaitUntilAsync(() => !host.Outcomes.Outcomes.IsEmpty);
        (string method, string path, int status, string? exceptionType) = Assert.Single(host.Outcomes.Outcomes);

        Assert.Equal("GET", method);
        Assert.Contains(Movie.ToString(), path);

        // A disconnecting client is not "the service is unavailable" - and not a server fault either.
        Assert.NotEqual(503, status);
        Assert.NotEqual(500, status);
        Assert.Null(exceptionType); // nothing escaped the pipeline

        // The framework recognizes a client abort itself, so the last-resort handler (which logs an Error and answers 500) never runs.
        Assert.DoesNotContain(host.Logs.Entries, entry => entry.Contains("Unhandled exception processing", StringComparison.Ordinal));
        Assert.DoesNotContain(host.Logs.Entries, entry => entry.Contains("GlobalExceptionHandler", StringComparison.Ordinal));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime giveUp = DateTime.UtcNow.AddSeconds(10); // a failure guard only

        while (!condition())
        {
            Assert.True(DateTime.UtcNow < giveUp, "Timed out waiting for the request to finish on the server.");
            await Task.Delay(1);
        }
    }
}
