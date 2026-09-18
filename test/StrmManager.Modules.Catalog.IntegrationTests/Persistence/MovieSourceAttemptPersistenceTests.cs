using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using StrmManager.Modules.Catalog.Domain.Movies;
using StrmManager.Modules.Catalog.Domain.Shared;
using StrmManager.Modules.Catalog.Domain.SourceAttempts;
using StrmManager.Modules.Catalog.Infrastructure.Database;
using StrmManager.Modules.Catalog.IntegrationTests.Infrastructure;

namespace StrmManager.Modules.Catalog.IntegrationTests.Persistence;

/// <summary>
/// Movie half of SourceAttempt persistence (SourceAttempt.ForMovie / GetForMovieAsync /
/// the source_attempts.MovieId FK) - schema and code existed since Phase 1 but were never
/// exercised. Nothing here may ever need, or store, a stream URL.
/// </summary>
public class MovieSourceAttemptPersistenceTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly DateTime UtcNow = new(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);

    private static Movie NewMovie() =>
        Movie.Schedule(
            new ExternalIds($"tt{Guid.NewGuid():N}"[..14], null, null),
            "Attempt Movie",
            2025,
            TimeSpan.FromMinutes(100),
            UtcNow.AddDays(-1),
            UtcNow.AddDays(-1));

    private async Task<Guid> SeedMovieAsync()
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        Movie movie = NewMovie();
        context.Set<Movie>().Add(movie);
        await context.SaveChangesAsync();

        return movie.Id;
    }

    private async Task InsertAsync(params SourceAttempt[] attempts)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ISourceAttemptRepository repository = scope.ServiceProvider.GetRequiredService<ISourceAttemptRepository>();
        CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        foreach (SourceAttempt attempt in attempts)
        {
            repository.Insert(attempt);
        }

        await context.SaveChangesAsync();
    }

    private static SourceAttempt Attempt(Guid movieId, DateTime at, string name, SourceAttemptResult result = SourceAttemptResult.Approved) =>
        SourceAttempt.ForMovie(movieId, "FrostStream", name, at, result, null, null, null, null, null, null);

    [Fact]
    public async Task ForMovie_PersistsWithMovieIdAndNoEpisodeId_AndRoundTripsEveryField()
    {
        Guid movieId = await SeedMovieAsync();
        SourceAttempt attempt = SourceAttempt.ForMovie(
            movieId,
            provider: "FrostStream",
            sourceName: "Attempt Movie (2025) 1080p",
            utcNow: UtcNow,
            result: SourceAttemptResult.Approved,
            duration: TimeSpan.FromMinutes(101) + TimeSpan.FromSeconds(30),
            expectedDuration: TimeSpan.FromMinutes(100),
            differencePercentage: 1.5,
            videoCodec: "h264",
            audioCodec: "aac",
            failureReason: null);

        await InsertAsync(attempt);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ISourceAttemptRepository repository = scope.ServiceProvider.GetRequiredService<ISourceAttemptRepository>();
        SourceAttempt persisted = Assert.Single(await repository.GetForMovieAsync(movieId));

        Assert.Equal(attempt.Id, persisted.Id);
        Assert.Equal(movieId, persisted.MovieId);
        Assert.Null(persisted.EpisodeId);
        Assert.Equal("FrostStream", persisted.Provider);
        Assert.Equal("Attempt Movie (2025) 1080p", persisted.SourceName);
        Assert.Equal(UtcNow, persisted.AttemptedAtUtc);
        Assert.Equal(SourceAttemptResult.Approved, persisted.Result);
        Assert.Equal(TimeSpan.FromMinutes(101) + TimeSpan.FromSeconds(30), persisted.Duration);
        Assert.Equal(TimeSpan.FromMinutes(100), persisted.ExpectedDuration);
        Assert.Equal(1.5, persisted.DifferencePercentage);
        Assert.Equal("h264", persisted.VideoCodec);
        Assert.Equal("aac", persisted.AudioCodec);
        Assert.Null(persisted.FailureReason);
    }

    [Fact]
    public async Task ForMovie_RejectedAttemptWithFailureReason_RoundTripsWithNullMediaDetails()
    {
        Guid movieId = await SeedMovieAsync();
        await InsertAsync(SourceAttempt.ForMovie(
            movieId, "FrostStream", "Wrong Movie (1999)", UtcNow, SourceAttemptResult.Rejected,
            null, null, null, null, null, "Movie identity mismatch"));

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        SourceAttempt persisted = Assert.Single(
            await scope.ServiceProvider.GetRequiredService<ISourceAttemptRepository>().GetForMovieAsync(movieId));

        Assert.Equal(SourceAttemptResult.Rejected, persisted.Result);
        Assert.Equal("Movie identity mismatch", persisted.FailureReason);
        Assert.Null(persisted.Duration);
        Assert.Null(persisted.VideoCodec);
    }

    [Fact]
    public async Task GetForMovie_ReturnsOnlyThatMoviesAttempts_OrderedByAttemptedAtUtcAscending()
    {
        // Documented ordering (SourceAttemptRepository): OrderBy AttemptedAtUtc ascending -
        // insertion order below is deliberately not chronological.
        Guid movieId = await SeedMovieAsync();
        Guid otherMovieId = await SeedMovieAsync();

        await InsertAsync(
            Attempt(movieId, UtcNow.AddMinutes(2), "third"),
            Attempt(movieId, UtcNow, "first"),
            Attempt(otherMovieId, UtcNow.AddMinutes(1), "belongs to another movie"),
            Attempt(movieId, UtcNow.AddMinutes(1), "second"));

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        IReadOnlyList<SourceAttempt> attempts =
            await scope.ServiceProvider.GetRequiredService<ISourceAttemptRepository>().GetForMovieAsync(movieId);

        Assert.Equal(["first", "second", "third"], attempts.Select(a => a.SourceName).ToArray());
        Assert.All(attempts, a => Assert.Equal(movieId, a.MovieId));
    }

    [Fact]
    public async Task GetForMovie_WithNoAttempts_ReturnsEmpty()
    {
        Guid movieId = await SeedMovieAsync();

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();

        Assert.Empty(await scope.ServiceProvider.GetRequiredService<ISourceAttemptRepository>().GetForMovieAsync(movieId));
    }

    [Fact]
    public async Task ForMovie_WithUnknownMovieId_IsRejectedByTheForeignKey()
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ISourceAttemptRepository>()
            .Insert(Attempt(Guid.NewGuid(), UtcNow, "orphan"));

        await Assert.ThrowsAsync<DbUpdateException>(
            () => scope.ServiceProvider.GetRequiredService<CatalogDbContext>().SaveChangesAsync());
    }

    [Fact]
    public async Task SourceAttempt_StoresNoStreamUrlColumnAnywhere()
    {
        // URL-safety guarantee (ADR-011): an attempt records why a source was accepted or
        // rejected, never where it lives. Guard the persisted shape itself, not just what
        // this test happened to write - if a URL-bearing column is ever added, this fails.
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        IEntityType entityType = context.Model.FindEntityType(typeof(SourceAttempt))!;
        string[] columns = entityType.GetProperties().Select(p => p.Name).ToArray();

        Assert.DoesNotContain(columns, c =>
            c.Contains("Url", StringComparison.OrdinalIgnoreCase) ||
            c.Contains("Uri", StringComparison.OrdinalIgnoreCase) ||
            c.Contains("Link", StringComparison.OrdinalIgnoreCase));
    }
}
