using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Application.Validation;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Common.Presentation.ApiResults;
using StrmManager.Common.Presentation.Endpoints;
using StrmManager.Modules.Catalog.Application.Series.GetSeries;
using StrmManager.Modules.Catalog.Application.Series.GetSeriesByImdbId;
using ApiResults = StrmManager.Common.Presentation.ApiResults.ApiResults;
using HttpResults = Microsoft.AspNetCore.Http.Results;

namespace StrmManager.Modules.Catalog.Presentation.Series;

/// <summary>
/// GET api/series/by-imdb/{imdbId}: finds an existing series by IMDb id.
/// Read-only: it never creates, refreshes or changes the series.
/// </summary>
internal sealed class GetSeriesByImdbId : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("api/series/by-imdb/{imdbId}", async (
            string imdbId,
            IValidator<GetSeriesByImdbIdQuery> validator,
            IQueryHandler<GetSeriesByImdbIdQuery, SeriesResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var query = new GetSeriesByImdbIdQuery(imdbId);

            ValidationResult validation =
                await validator.ValidateAsync(query, cancellationToken);

            if (!validation.IsValid)
            {
                return ApiResults.Problem(
                    validation.ToApplicationResult());
            }

            Result<SeriesResponse> result =
                await handler.Handle(query, cancellationToken);

            return result.Match(
                HttpResults.Ok,
                ApiResults.Problem);
        });
    }
}
