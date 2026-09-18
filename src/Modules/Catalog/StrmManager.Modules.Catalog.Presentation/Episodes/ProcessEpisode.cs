using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Common.Presentation.ApiResults;
using StrmManager.Common.Presentation.Endpoints;
using StrmManager.Modules.Catalog.Application.Episodes.ProcessEpisode;
using ApiResults = StrmManager.Common.Presentation.ApiResults.ApiResults;
using HttpResults = Microsoft.AspNetCore.Http.Results;

namespace StrmManager.Modules.Catalog.Presentation.Episodes;

internal sealed class ProcessEpisode : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("api/episodes/{id:guid}/process", async (
            Guid id,
            ICommandHandler<ProcessEpisodeCommand, ProcessEpisodeResult> handler,
            CancellationToken cancellationToken) =>
        {
            Result<ProcessEpisodeResult> result = await handler.Handle(new ProcessEpisodeCommand(id), cancellationToken);

            return result.Match(HttpResults.Ok, ApiResults.Problem);
        });
    }
}
