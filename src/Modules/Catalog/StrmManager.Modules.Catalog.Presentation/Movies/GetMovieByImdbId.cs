using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Application.Validation;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Common.Presentation.ApiResults;
using StrmManager.Common.Presentation.Endpoints;
using StrmManager.Modules.Catalog.Application.Movies.GetMovie;
using StrmManager.Modules.Catalog.Application.Movies.GetMovieByImdbId;
using ApiResults = StrmManager.Common.Presentation.ApiResults.ApiResults;
using HttpResults = Microsoft.AspNetCore.Http.Results;

namespace StrmManager.Modules.Catalog.Presentation.Movies;

/// <summary>
/// GET api/movies/by-imdb/{imdbId}: find a movie's id and state from its IMDb id. 200 with the same MovieResponse as GET api/movies/{id},
/// 404 when there is no such movie, 400 for a malformed IMDb id. Read-only - it never calls Cinemeta or a stream provider and never
/// changes anything. The literal "by-imdb" segment cannot be mistaken for the {id:guid} route.
///
/// Validation is local to this endpoint (there is no general query-validation pipeline): the same shared rule AddMovie uses, run before the
/// handler, so an invalid id has no effect at all.
/// </summary>
internal sealed class GetMovieByImdbId : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("api/movies/by-imdb/{imdbId}", async (
            string imdbId,
            IValidator<GetMovieByImdbIdQuery> validator,
            IQueryHandler<GetMovieByImdbIdQuery, MovieResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var query = new GetMovieByImdbIdQuery(imdbId);

            ValidationResult validation = await validator.ValidateAsync(query, cancellationToken);

            if (!validation.IsValid)
            {
                return ApiResults.Problem(validation.ToApplicationResult());
            }

            Result<MovieResponse> result = await handler.Handle(query, cancellationToken);

            return result.Match(HttpResults.Ok, ApiResults.Problem);
        });
    }
}
