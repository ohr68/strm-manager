using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Common.Presentation.ApiResults;
using StrmManager.Common.Presentation.Endpoints;
using StrmManager.Modules.Catalog.Application.Episodes.RetryEpisode;
using ApiResults = StrmManager.Common.Presentation.ApiResults.ApiResults;
using HttpResults = Microsoft.AspNetCore.Http.Results;

namespace StrmManager.Modules.Catalog.Presentation.Episodes;

internal sealed class RetryEpisode : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("api/episodes/{id:guid}/retry", async (
            Guid id,
            ICommandHandler<RetryEpisodeCommand, RetryEpisodeResult> handler,
            CancellationToken cancellationToken) =>
        {
            Result<RetryEpisodeResult> result = await handler.Handle(new RetryEpisodeCommand(id), cancellationToken);

            return result.Match(HttpResults.Ok, ApiResults.Problem);
        });
    }
}
