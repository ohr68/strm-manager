using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Metadata;

namespace StrmManager.Modules.Catalog.IntegrationTests.Infrastructure;

/// <summary>
/// Test double for IMetadataProvider - keeps integration tests off live Cinemeta.
/// Defaults to "not found" for everything, so tests that don't care about metadata
/// sync (health checks, plain series CRUD, relationship constraints) get deterministic,
/// network-free behavior without configuring anything.
/// </summary>
public sealed class FakeMetadataProvider : IMetadataProvider
{
    public Func<string, Result<SeriesMetadata>> Handler { get; set; } =
        externalId => Result.Failure<SeriesMetadata>(MetadataProviderErrors.SeriesNotFound("Fake", externalId));

    public Func<string, Result<MovieMetadata>> MovieHandler { get; set; } =
        externalId => Result.Failure<MovieMetadata>(MetadataProviderErrors.MovieNotFound("Fake", externalId));

    public Func<string, Result<MovieDetail>> MovieDetailHandler { get; set; } =
        externalId => Result.Failure<MovieDetail>(MetadataProviderErrors.MovieNotFound("Fake", externalId));

    public int MovieDetailCallCount { get; private set; }

    public Task<Result<SeriesMetadata>> GetSeriesAsync(string externalId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Handler(externalId));

    public Task<Result<MovieMetadata>> GetMovieAsync(string externalId, CancellationToken cancellationToken = default) =>
        Task.FromResult(MovieHandler(externalId));

    public Task<Result<MovieDetail>> GetMovieDetailAsync(string externalId, CancellationToken cancellationToken = default)
    {
        MovieDetailCallCount++;
        return Task.FromResult(MovieDetailHandler(externalId));
    }
}
