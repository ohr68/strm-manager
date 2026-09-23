using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Common.Presentation.ApiResults;
using StrmManager.Common.Presentation.Endpoints;
using StrmManager.Modules.Catalog.Application.Catalog.GetMovieRows;
using ApiResults = StrmManager.Common.Presentation.ApiResults.ApiResults;
using HttpResults = Microsoft.AspNetCore.Http.Results;

namespace StrmManager.Modules.Catalog.Presentation.Catalog;

internal sealed class GetMovieRows : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("api/catalog/movies/rows", async (
            IQueryHandler<GetMovieRowsQuery, MovieRowsResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<MovieRowsResponse> result = await handler.Handle(new GetMovieRowsQuery(), cancellationToken);

            return result.Match(HttpResults.Ok, ApiResults.Problem);
        });
    }
}
