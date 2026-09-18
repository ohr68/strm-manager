using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Common.Presentation.ApiResults;
using StrmManager.Common.Presentation.Endpoints;
using StrmManager.Modules.Catalog.Application.Movies.GetMovie;
using ApiResults = StrmManager.Common.Presentation.ApiResults.ApiResults;
using HttpResults = Microsoft.AspNetCore.Http.Results;

namespace StrmManager.Modules.Catalog.Presentation.Movies;

internal sealed class GetMovie : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("api/movies/{id:guid}", async (
            Guid id,
            IQueryHandler<GetMovieQuery, MovieResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<MovieResponse> result = await handler.Handle(new GetMovieQuery(id), cancellationToken);

            return result.Match(HttpResults.Ok, ApiResults.Problem);
        });
    }
}
