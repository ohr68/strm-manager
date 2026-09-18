using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Common.Presentation.ApiResults;
using StrmManager.Common.Presentation.Endpoints;
using StrmManager.Modules.Catalog.Application.Metadata;
using StrmManager.Modules.Catalog.Application.Series.RefreshSeriesMetadata;
using ApiResults = StrmManager.Common.Presentation.ApiResults.ApiResults;
using HttpResults = Microsoft.AspNetCore.Http.Results;

namespace StrmManager.Modules.Catalog.Presentation.Series;

internal sealed class RefreshSeriesMetadata : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("api/series/{id:guid}/refresh", async (
            Guid id,
            ICommandHandler<RefreshSeriesMetadataCommand, CatalogSynchronizationResult> handler,
            CancellationToken cancellationToken) =>
        {
            Result<CatalogSynchronizationResult> result = await handler.Handle(new RefreshSeriesMetadataCommand(id), cancellationToken);

            return result.Match(HttpResults.Ok, ApiResults.Problem);
        });
    }
}
