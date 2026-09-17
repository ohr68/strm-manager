using Microsoft.EntityFrameworkCore;
using StrmManager.Api.Middleware;
using StrmManager.Common.Presentation.Endpoints;
using StrmManager.Modules.Catalog.Infrastructure;
using StrmManager.Modules.Catalog.Infrastructure.Database;
using StrmManager.Modules.Catalog.Presentation;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddCatalogModule(builder.Configuration);
builder.Services.AddEndpoints(AssemblyReference.Assembly);

builder.Services
    .AddHealthChecks()
    .AddDbContextCheck<CatalogDbContext>("catalog-database");

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
// startup is the intended schema management strategy in every environment.
using (IServiceScope scope = app.Services.CreateScope())
{
    CatalogDbContext dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.MapEndpoints();
app.MapHealthChecks("/health");

app.Run();

public partial class Program;
