using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Application.Validation;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Common.Presentation.ApiResults;
using StrmManager.Common.Presentation.Endpoints;
using StrmManager.Modules.Catalog.Application.Catalog.GetMovieDetail;
using ApiResults = StrmManager.Common.Presentation.ApiResults.ApiResults;
using HttpResults = Microsoft.AspNetCore.Http.Results;

namespace StrmManager.Modules.Catalog.Presentation.Catalog;

/// <summary>
/// GET api/catalog/movies/{imdbId}/detail (UI-6b) - read-only Movie Detail metadata for a selected catalog/search
/// card. Metadata-only, exactly like Popular/Rows/Search: no AddMovie/EnsureMovie/ProcessMovie/scan happens by
/// selecting a movie. Validation is local to this endpoint (there is no general query-validation pipeline),
/// matching GetMovieByImdbId/SearchMovies - the same shared IMDb id rule runs before the handler, so an invalid id
/// has no effect at all.
/// </summary>
internal sealed class GetMovieDetail : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("api/catalog/movies/{imdbId}/detail", async (
            string imdbId,
            IValidator<GetMovieDetailQuery> validator,
            IQueryHandler<GetMovieDetailQuery, MovieDetailResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var query = new GetMovieDetailQuery(imdbId);

            ValidationResult validation = await validator.ValidateAsync(query, cancellationToken);

            if (!validation.IsValid)
            {
                return ApiResults.Problem(validation.ToApplicationResult());
            }

            Result<MovieDetailResponse> result = await handler.Handle(query, cancellationToken);

            return result.Match(HttpResults.Ok, ApiResults.Problem);
        });
    }
}
