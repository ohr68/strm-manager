using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StrmManager.Modules.Catalog.Domain.Movies;
using StrmManager.Modules.Catalog.Domain.Shared;
using StrmManager.Modules.Catalog.Domain.StrmFiles;
using StrmManager.Modules.Catalog.Infrastructure.Database;
using StrmManager.Modules.Catalog.IntegrationTests.Infrastructure;

namespace StrmManager.Modules.Catalog.IntegrationTests.Persistence;

/// <summary>
/// Movie half of StrmFile persistence (StrmFile.ForMovie / GetForMovieAsync / Overwrite,
/// the strm_files.MovieId FK and its unique index) - schema and code existed since Phase 1
/// but were never exercised.
/// </summary>
public class MovieStrmFilePersistenceTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly DateTime UtcNow = new(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);

    private async Task<Guid> SeedMovieAsync()
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        Movie movie = Movie.Schedule(
            new ExternalIds($"tt{Guid.NewGuid():N}"[..14], null, null),
            "Strm Movie",
            2025,
            TimeSpan.FromMinutes(100),
            UtcNow.AddDays(-1),
            UtcNow.AddDays(-1));
        context.Set<Movie>().Add(movie);
        await context.SaveChangesAsync();

        return movie.Id;
    }

    private async Task InsertAsync(StrmFile strmFile)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IStrmFileRepository>().Insert(strmFile);
        await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().SaveChangesAsync();
    }

    [Fact]
    public async Task ForMovie_PersistsWithMovieIdAndNoEpisodeId_AndRoundTrips()
    {
        Guid movieId = await SeedMovieAsync();
        StrmFile strmFile = StrmFile.ForMovie(movieId, "/data/movies/Strm Movie (2025)/Strm Movie (2025).strm", UtcNow);

        await InsertAsync(strmFile);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        StrmFile? persisted = await scope.ServiceProvider.GetRequiredService<IStrmFileRepository>().GetForMovieAsync(movieId);

        Assert.NotNull(persisted);
        Assert.Equal(strmFile.Id, persisted.Id);
        Assert.Equal(movieId, persisted.MovieId);
        Assert.Null(persisted.EpisodeId);
        Assert.Equal("/data/movies/Strm Movie (2025)/Strm Movie (2025).strm", persisted.Path);
        Assert.Equal(UtcNow, persisted.CreatedAtUtc);
        Assert.Equal(UtcNow, persisted.UpdatedAtUtc);
    }

    [Fact]
    public async Task GetForMovie_WithNoStrmFile_ReturnsNull()
    {
        Guid movieId = await SeedMovieAsync();

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();

        Assert.Null(await scope.ServiceProvider.GetRequiredService<IStrmFileRepository>().GetForMovieAsync(movieId));
    }

    [Fact]
    public async Task GetForMovie_ReturnsOnlyThatMoviesStrmFile()
    {
        Guid movieId = await SeedMovieAsync();
        Guid otherMovieId = await SeedMovieAsync();
        await InsertAsync(StrmFile.ForMovie(movieId, "/data/movies/a.strm", UtcNow));
        await InsertAsync(StrmFile.ForMovie(otherMovieId, "/data/movies/b.strm", UtcNow));

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        StrmFile? persisted = await scope.ServiceProvider.GetRequiredService<IStrmFileRepository>().GetForMovieAsync(movieId);

        Assert.Equal("/data/movies/a.strm", persisted!.Path);
    }

    [Fact]
    public async Task TwoMoviesEachWithAStrmFile_AreNotBlockedByTheNullEpisodeIdOnTheOtherUniqueIndex()
    {
        // strm_files has a unique index on EpisodeId AND one on MovieId; every movie row
        // has a NULL EpisodeId. SQLite must treat those NULLs as distinct - otherwise only
        // one movie could ever have a StrmFile.
        Guid firstMovieId = await SeedMovieAsync();
        Guid secondMovieId = await SeedMovieAsync();

        await InsertAsync(StrmFile.ForMovie(firstMovieId, "/data/movies/first.strm", UtcNow));
        await InsertAsync(StrmFile.ForMovie(secondMovieId, "/data/movies/second.strm", UtcNow));

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        IStrmFileRepository repository = scope.ServiceProvider.GetRequiredService<IStrmFileRepository>();
        Assert.NotNull(await repository.GetForMovieAsync(firstMovieId));
        Assert.NotNull(await repository.GetForMovieAsync(secondMovieId));
    }

    [Fact]
    public async Task ASecondStrmFileForTheSameMovie_IsRejectedByTheUniqueIndex()
    {
        Guid movieId = await SeedMovieAsync();
        await InsertAsync(StrmFile.ForMovie(movieId, "/data/movies/original.strm", UtcNow));

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IStrmFileRepository>()
            .Insert(StrmFile.ForMovie(movieId, "/data/movies/duplicate.strm", UtcNow));

        await Assert.ThrowsAsync<DbUpdateException>(
            () => scope.ServiceProvider.GetRequiredService<CatalogDbContext>().SaveChangesAsync());

        await using AsyncServiceScope verifyScope = factory.Services.CreateAsyncScope();
        StrmFile? persisted = await verifyScope.ServiceProvider.GetRequiredService<IStrmFileRepository>().GetForMovieAsync(movieId);
        Assert.Equal("/data/movies/original.strm", persisted!.Path);
    }

    [Fact]
    public async Task ForMovie_WithUnknownMovieId_IsRejectedByTheForeignKey()
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IStrmFileRepository>()
            .Insert(StrmFile.ForMovie(Guid.NewGuid(), "/data/movies/orphan.strm", UtcNow));

        await Assert.ThrowsAsync<DbUpdateException>(
            () => scope.ServiceProvider.GetRequiredService<CatalogDbContext>().SaveChangesAsync());
    }

    [Fact]
    public async Task Overwrite_UpdatesThePathInPlace_WithoutCreatingASecondRecord()
    {
        Guid movieId = await SeedMovieAsync();
        StrmFile original = StrmFile.ForMovie(movieId, "/data/movies/old.strm", UtcNow);
        await InsertAsync(original);
        DateTime overwrittenAtUtc = UtcNow.AddHours(1);

        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            StrmFile loaded = (await scope.ServiceProvider.GetRequiredService<IStrmFileRepository>().GetForMovieAsync(movieId))!;
            loaded.Overwrite("/data/movies/new.strm", overwrittenAtUtc);
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().SaveChangesAsync();
        }

        await using AsyncServiceScope verifyScope = factory.Services.CreateAsyncScope();
        CatalogDbContext context = verifyScope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        StrmFile persisted = (await verifyScope.ServiceProvider.GetRequiredService<IStrmFileRepository>().GetForMovieAsync(movieId))!;

        Assert.Equal(original.Id, persisted.Id); // same row, not a replacement
        Assert.Equal("/data/movies/new.strm", persisted.Path);
        Assert.Equal(UtcNow, persisted.CreatedAtUtc);
        Assert.Equal(overwrittenAtUtc, persisted.UpdatedAtUtc);
        Assert.Equal(1, await context.Set<StrmFile>().CountAsync(f => f.MovieId == movieId));
    }
}
