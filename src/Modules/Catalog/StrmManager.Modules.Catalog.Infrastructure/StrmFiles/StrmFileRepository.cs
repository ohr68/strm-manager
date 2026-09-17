using Microsoft.EntityFrameworkCore;
using StrmManager.Modules.Catalog.Domain.StrmFiles;
using StrmManager.Modules.Catalog.Infrastructure.Database;

namespace StrmManager.Modules.Catalog.Infrastructure.StrmFiles;

internal sealed class StrmFileRepository(CatalogDbContext context) : IStrmFileRepository
{
    public async Task<StrmFile?> GetForEpisodeAsync(Guid episodeId, CancellationToken cancellationToken = default) =>
        await context.StrmFiles.SingleOrDefaultAsync(strmFile => strmFile.EpisodeId == episodeId, cancellationToken);

    public async Task<StrmFile?> GetForMovieAsync(Guid movieId, CancellationToken cancellationToken = default) =>
        await context.StrmFiles.SingleOrDefaultAsync(strmFile => strmFile.MovieId == movieId, cancellationToken);

    public void Insert(StrmFile strmFile) => context.StrmFiles.Add(strmFile);
}
