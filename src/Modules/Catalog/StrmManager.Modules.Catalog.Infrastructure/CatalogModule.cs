using FluentValidation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StrmManager.Common.Application.Messaging;
using StrmManager.Modules.Catalog.Application.Abstractions.Data;
using StrmManager.Modules.Catalog.Application.Series.AddSeries;
using StrmManager.Modules.Catalog.Domain.Episodes;
using StrmManager.Modules.Catalog.Domain.Movies;
using StrmManager.Modules.Catalog.Domain.Seasons;
using StrmManager.Modules.Catalog.Domain.SourceAttempts;
using StrmManager.Modules.Catalog.Domain.StrmFiles;
using StrmManager.Modules.Catalog.Infrastructure.Database;
using StrmManager.Modules.Catalog.Infrastructure.Episodes;
using StrmManager.Modules.Catalog.Infrastructure.Movies;
using StrmManager.Modules.Catalog.Infrastructure.Seasons;
using StrmManager.Modules.Catalog.Infrastructure.SourceAttempts;
using StrmManager.Modules.Catalog.Infrastructure.StrmFiles;
using ISeriesRepository = StrmManager.Modules.Catalog.Domain.Series.ISeriesRepository;
using SeriesRepository = StrmManager.Modules.Catalog.Infrastructure.Series.SeriesRepository;

namespace StrmManager.Modules.Catalog.Infrastructure;

public static class CatalogModule
{
    public static IServiceCollection AddCatalogModule(this IServiceCollection services, IConfiguration configuration)
    {
        string connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("The 'Database' connection string is not configured.");

        // Microsoft.Data.Sqlite does not enforce FK constraints unless explicitly asked
        // to - without this, the Restrict/cascade behaviors configured on the
        // Series/Season/Episode relationships would be silently ignored at runtime.
        var sqliteConnectionStringBuilder = new SqliteConnectionStringBuilder(connectionString)
        {
            ForeignKeys = true,
        };

        services.AddDbContext<CatalogDbContext>(options => options.UseSqlite(sqliteConnectionStringBuilder.ConnectionString));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CatalogDbContext>());

        services.AddScoped<ISeriesRepository, SeriesRepository>();
        services.AddScoped<ISeasonRepository, SeasonRepository>();
        services.AddScoped<IEpisodeRepository, EpisodeRepository>();
        services.AddScoped<IMovieRepository, MovieRepository>();
        services.AddScoped<ISourceAttemptRepository, SourceAttemptRepository>();
        services.AddScoped<IStrmFileRepository, StrmFileRepository>();

        services.AddHandlersFromAssembly(typeof(AddSeriesCommand).Assembly);
        services.AddValidatorsFromAssembly(typeof(AddSeriesCommand).Assembly, includeInternalTypes: true);

        return services;
    }
}
