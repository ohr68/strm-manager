using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Metadata;
using StrmManager.Modules.Catalog.Infrastructure.Metadata.Cinemeta;

namespace StrmManager.Modules.Catalog.UnitTests.Metadata;

/// <summary>
/// Focused unit tests for CinemetaMetadataProvider.GetMovieDetailAsync (UI-6b) - shares GetMovieAsync's exact
/// HTTP/error-handling path (same GetMetaAsync helper), so only what's NEW/different is covered here: the mapped
/// display fields, and that the not-found/unreachable taxonomy still applies unchanged. The full malformed-response
/// matrix (already proven for this same code path by CinemetaMovieProviderTests) is not reproduced.
/// </summary>
public class CinemetaMovieDetailProviderTests
{
    private static CinemetaMetadataProvider CreateProvider(FakeHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://cinemeta.example.invalid/"),
            Timeout = TimeSpan.FromSeconds(30),
        };

        return new CinemetaMetadataProvider(httpClient, NullLogger<CinemetaMetadataProvider>.Instance);
    }

    private static string LoadFixture(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Metadata", "Fixtures", fileName));

    [Fact]
    public async Task GetMovieDetailAsync_SuccessfulResponse_ReturnsMappedDisplayFields()
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(LoadFixture("movie-detail-dark-knight.json")),
        }));

        Result<MovieDetail> result = await CreateProvider(handler).GetMovieDetailAsync("tt0468569");

        Assert.True(result.IsSuccess);
        MovieDetail detail = result.Value;
        Assert.Equal("The Dark Knight", detail.Title);
        Assert.Equal(2008, detail.Year);
        Assert.Equal(TimeSpan.FromMinutes(152), detail.Runtime);
        Assert.Contains("Joker", detail.Description);
        Assert.Equal(["Action", "Crime", "Drama"], detail.Genres);
        Assert.Equal("9.1", detail.ImdbRating);
        Assert.Equal("https://images.metahub.space/poster/small/tt0468569/img", detail.PosterUrl);
        Assert.Equal("https://images.metahub.space/background/medium/tt0468569/img", detail.BackdropUrl);
    }

    [Fact]
    public async Task GetMovieDetailAsync_RequestsTheMovieMetaEndpointWithTheEscapedId()
    {
        HttpRequestMessage? captured = null;
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            captured = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(LoadFixture("movie-detail-dark-knight.json")),
            });
        });

        await CreateProvider(handler).GetMovieDetailAsync("tt0468569");

        Assert.NotNull(captured);
        Assert.Equal("https://cinemeta.example.invalid/meta/movie/tt0468569.json", captured.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task GetMovieDetailAsync_Http200IdOnlyStub_ReturnsMovieNotFound()
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(LoadFixture("movie-id-only-stub.json")),
        }));

        Result<MovieDetail> result = await CreateProvider(handler).GetMovieDetailAsync("tt0000000");

        Assert.True(result.IsFailure);
        Assert.Equal("Metadata.MovieNotFound", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public async Task GetMovieDetailAsync_NotFoundStatus_ReturnsMovieNotFound()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        Result<MovieDetail> result = await CreateProvider(handler).GetMovieDetailAsync("tt0000000");

        Assert.True(result.IsFailure);
        Assert.Equal("Metadata.MovieNotFound", result.Error.Code);
    }

    [Fact]
    public async Task GetMovieDetailAsync_ServerErrorStatus_ReturnsProviderUnavailable()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        Result<MovieDetail> result = await CreateProvider(handler).GetMovieDetailAsync("tt0468569");

        Assert.True(result.IsFailure);
        Assert.Equal("Metadata.ProviderUnavailable", result.Error.Code);
    }

    [Fact]
    public async Task GetMovieDetailAsync_NetworkFailure_ReturnsProviderUnavailable()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            throw new HttpRequestException("simulated DNS/connection failure"));

        Result<MovieDetail> result = await CreateProvider(handler).GetMovieDetailAsync("tt0468569");

        Assert.True(result.IsFailure);
        Assert.Equal("Metadata.ProviderUnavailable", result.Error.Code);
    }

    [Fact]
    public async Task GetMovieDetailAsync_CallerCancelsOwnToken_PropagatesOperationCanceledException()
    {
        var handler = new FakeHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var cancellationTokenSource = new CancellationTokenSource();
        Task<Result<MovieDetail>> task = CreateProvider(handler).GetMovieDetailAsync("tt0468569", cancellationTokenSource.Token);

        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }
}
