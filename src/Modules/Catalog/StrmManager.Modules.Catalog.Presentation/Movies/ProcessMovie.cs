using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Common.Presentation.ApiResults;
using StrmManager.Common.Presentation.Endpoints;
using StrmManager.Modules.Catalog.Application.Movies.ProcessMovie;
using ApiResults = StrmManager.Common.Presentation.ApiResults.ApiResults;
using HttpResults = Microsoft.AspNetCore.Http.Results;

namespace StrmManager.Modules.Catalog.Presentation.Movies;

internal sealed class ProcessMovie : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("api/movies/{id:guid}/process", async (
            Guid id,
            ICommandHandler<ProcessMovieCommand, ProcessMovieResult> handler,
            CancellationToken cancellationToken) =>
        {
            Result<ProcessMovieResult> result = await handler.Handle(new ProcessMovieCommand(id), cancellationToken);

            return result.Match(HttpResults.Ok, ApiResults.Problem);
        });
    }
}
