using Microsoft.EntityFrameworkCore;
using StrmManager.Modules.Catalog.Application.Abstractions.Data;
using StrmManager.Modules.Catalog.Domain.Episodes;
using StrmManager.Modules.Catalog.Domain.Movies;
using StrmManager.Modules.Catalog.Domain.Seasons;
using StrmManager.Modules.Catalog.Domain.SourceAttempts;
using StrmManager.Modules.Catalog.Domain.StrmFiles;
using SeriesEntity = StrmManager.Modules.Catalog.Domain.Series.Series;

namespace StrmManager.Modules.Catalog.Infrastructure.Database;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options)
    : DbContext(options), IUnitOfWork
{
    internal DbSet<SeriesEntity> Series { get; set; } = null!;

    internal DbSet<Season> Seasons { get; set; } = null!;

    internal DbSet<Episode> Episodes { get; set; } = null!;

    internal DbSet<Movie> Movies { get; set; } = null!;

    internal DbSet<SourceAttempt> SourceAttempts { get; set; } = null!;

    internal DbSet<StrmFile> StrmFiles { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);
    }
}
