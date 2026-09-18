using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StrmManager.Api.Middleware;
using StrmManager.Common.Presentation.Endpoints;
using StrmManager.Modules.Catalog.Infrastructure;
using StrmManager.Modules.Catalog.Infrastructure.Database;
using StrmManager.Modules.Catalog.Presentation;
using StrmManager.Modules.MediaProcessing.Infrastructure;
using StrmManager.Modules.MediaProcessing.Infrastructure.Validation.Ffprobe;
using StrmManager.Modules.Scheduling.Infrastructure;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddCatalogModule(builder.Configuration);
builder.Services.AddMediaProcessingModule(builder.Configuration);
// Registered after AddCatalogModule - its ISchedulerStatusProvider registration wins
// resolution over Catalog's own fallback (see CatalogModule/ADR-012).
builder.Services.AddSchedulingModule(builder.Configuration);
builder.Services.AddEndpoints(AssemblyReference.Assembly);

builder.Services
    .AddHealthChecks()
    .AddDbContextCheck<CatalogDbContext>("catalog-database")
    // Degraded, not Unhealthy, when ffprobe is missing - see FfprobeHealthCheck.
    .AddCheck<FfprobeHealthCheck>("ffprobe", failureStatus: HealthStatus.Degraded);

builder.Services.AddOpenApi();

// Global exception handling for unexpected technical failures only - expected
// domain/application failures never throw, they flow through Result/Error ->
// StrmManager.Common.Presentation.ApiResults instead (see ADR-005 and architecture.md).
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

WebApplication app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// This service owns its SQLite file end-to-end (no separate deploy-time migration
// step, unlike Evently's Postgres setup) - applying pending migrations on every
// startup is the intended schema management strategy in every environment. This also
// runs strictly before app.Run() starts any BackgroundService (Scheduling's workers),
// so they never race the schema - see ADR-014.
using (IServiceScope scope = app.Services.CreateScope())
{
    CatalogDbContext dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    await dbContext.Database.MigrateAsync();

    // WAL lets readers (GET /api/status, GET /api/episodes, ...) proceed without
    // blocking on a concurrent writer (a worker mid-tick) - now that the API and two
    // Scheduling BackgroundServices genuinely overlap. A no-op if already WAL (the mode
    // persists in the database file itself); run once at startup, never per-request.
    // See ADR-014.
    await dbContext.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
}

app.MapEndpoints();
app.MapHealthChecks("/health");

app.Run();

public partial class Program;
