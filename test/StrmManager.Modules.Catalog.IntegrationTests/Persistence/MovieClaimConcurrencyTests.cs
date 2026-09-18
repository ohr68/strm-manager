using Microsoft.Extensions.DependencyInjection;
using StrmManager.Modules.Catalog.Application.Abstractions.Data;
using StrmManager.Modules.Catalog.Domain.Movies;
using StrmManager.Modules.Catalog.Domain.Shared;
using StrmManager.Modules.Catalog.Infrastructure.Database;
using StrmManager.Modules.Catalog.IntegrationTests.Infrastructure;

namespace StrmManager.Modules.Catalog.IntegrationTests.Persistence;

/// <summary>
/// Proves Movie.UpdatedAtUtc really is an optimistic-concurrency token by racing two
/// independent DbContexts (i.e. two requests, each with its own scope) for the same
/// Pending movie - the single-winner claim a future ProcessMovie handler relies on
/// (StartSearching -> IUnitOfWork.TrySaveChangesAsync), same mechanism as Episode
/// (ADR-013). Asserts behavior, not model metadata.
/// </summary>
public class MovieClaimConcurrencyTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly DateTime ReleasedAtUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime FirstClaimAtUtc = new(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SecondClaimAtUtc = FirstClaimAtUtc.AddSeconds(1);

    private async Task<Guid> SeedPendingMovieAsync()
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        Movie movie = Movie.Schedule(
            new ExternalIds($"tt{Guid.NewGuid():N}"[..14], null, null),
            "Claim Race Movie",
            2025,
            TimeSpan.FromMinutes(100),
            ReleasedAtUtc,
            ReleasedAtUtc);
        Assert.Equal(MediaStatus.Pending, movie.Status);

        context.Set<Movie>().Add(movie);
        await context.SaveChangesAsync();

        return movie.Id;
    }

    [Fact]
    public async Task StartSearchingClaim_TwoContextsLoadingTheSamePendingMovie_ExactlyOneWins()
    {
        Guid movieId = await SeedPendingMovieAsync();

        await using AsyncServiceScope scopeA = factory.Services.CreateAsyncScope();
        await using AsyncServiceScope scopeB = factory.Services.CreateAsyncScope();
        IMovieRepository repositoryA = scopeA.ServiceProvider.GetRequiredService<IMovieRepository>();
        IMovieRepository repositoryB = scopeB.ServiceProvider.GetRequiredService<IMovieRepository>();
        IUnitOfWork unitOfWorkA = scopeA.ServiceProvider.GetRequiredService<IUnitOfWork>();
        IUnitOfWork unitOfWorkB = scopeB.ServiceProvider.GetRequiredService<IUnitOfWork>();
        Assert.NotSame(unitOfWorkA, unitOfWorkB); // genuinely independent contexts

        Movie movieA = (await repositoryA.GetAsync(movieId))!;
        Movie movieB = (await repositoryB.GetAsync(movieId))!;
        Assert.NotSame(movieA, movieB);
        Assert.Equal(MediaStatus.Pending, movieA.Status);
        Assert.Equal(MediaStatus.Pending, movieB.Status);

        // Both callers observed Pending and both transition it in memory...
        Assert.True(movieA.StartSearching(FirstClaimAtUtc).IsSuccess);
        Assert.True(movieB.StartSearching(SecondClaimAtUtc).IsSuccess);

        // ...but only the first to save wins the claim.
        Assert.True(await unitOfWorkA.TrySaveChangesAsync());
        Assert.False(await unitOfWorkB.TrySaveChangesAsync());

        // The loser's write must not have overwritten the winner's.
        await using AsyncServiceScope verifyScope = factory.Services.CreateAsyncScope();
        Movie persisted = (await verifyScope.ServiceProvider.GetRequiredService<IMovieRepository>().GetAsync(movieId))!;
        Assert.Equal(MediaStatus.Searching, persisted.Status);
        Assert.Equal(FirstClaimAtUtc, persisted.UpdatedAtUtc);
    }

    [Fact]
    public async Task StartSearchingClaim_AfterTheWinnerCommitted_ALaterLoadSeesSearchingAndCannotClaimAgain()
    {
        Guid movieId = await SeedPendingMovieAsync();

        await using (AsyncServiceScope winnerScope = factory.Services.CreateAsyncScope())
        {
            Movie winner = (await winnerScope.ServiceProvider.GetRequiredService<IMovieRepository>().GetAsync(movieId))!;
            winner.StartSearching(FirstClaimAtUtc);
            Assert.True(await winnerScope.ServiceProvider.GetRequiredService<IUnitOfWork>().TrySaveChangesAsync());
        }

        await using AsyncServiceScope laterScope = factory.Services.CreateAsyncScope();
        Movie later = (await laterScope.ServiceProvider.GetRequiredService<IMovieRepository>().GetAsync(movieId))!;

        Assert.Equal(MediaStatus.Searching, later.Status);
        Assert.True(later.StartSearching(SecondClaimAtUtc).IsFailure); // domain guard, no save needed
    }

    [Fact]
    public async Task SequentialTransitionsInOneContext_DoNotSpuriouslyConflictWithThemselves()
    {
        // The token must refresh after each successful save - the claim is followed by
        // Validating and a terminal save in the same scope by the future handler.
        Guid movieId = await SeedPendingMovieAsync();

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        Movie movie = (await scope.ServiceProvider.GetRequiredService<IMovieRepository>().GetAsync(movieId))!;
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        Assert.True(movie.StartSearching(FirstClaimAtUtc).IsSuccess);
        Assert.True(await unitOfWork.TrySaveChangesAsync());

        Assert.True(movie.StartValidating(FirstClaimAtUtc.AddSeconds(1)).IsSuccess);
        Assert.True(await unitOfWork.TrySaveChangesAsync());

        Assert.True(movie.MarkCompleted(FirstClaimAtUtc.AddSeconds(2)).IsSuccess);
        Assert.True(await unitOfWork.TrySaveChangesAsync());

        await using AsyncServiceScope verifyScope = factory.Services.CreateAsyncScope();
        Movie persisted = (await verifyScope.ServiceProvider.GetRequiredService<IMovieRepository>().GetAsync(movieId))!;
        Assert.Equal(MediaStatus.Completed, persisted.Status);
        Assert.Equal(1, persisted.AttemptCount);
    }
}
