using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Application.Validation;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Common.Presentation.ApiResults;
using StrmManager.Common.Presentation.Endpoints;
using StrmManager.Modules.Catalog.Application.Catalog.SearchSeries;
using ApiResults =
    StrmManager.Common.Presentation.ApiResults.ApiResults;
using HttpResults =
    Microsoft.AspNetCore.Http.Results;

namespace StrmManager.Modules.Catalog.Presentation.Catalog;

internal sealed class SearchSeries : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet(
            "api/catalog/series/search",
            async (
                string? query,
                IValidator<SearchSeriesQuery> validator,
                IQueryHandler<
                    SearchSeriesQuery,
                    SearchSeriesResponse> handler,
                CancellationToken cancellationToken) =>
            {
                var searchQuery =
                    new SearchSeriesQuery(
                        (query ?? string.Empty).Trim());

                ValidationResult validation =
                    await validator.ValidateAsync(
                        searchQuery,
                        cancellationToken);

                if (!validation.IsValid)
                {
                    return ApiResults.Problem(
                        validation.ToApplicationResult());
                }

                Result<SearchSeriesResponse> result =
                    await handler.Handle(
                        searchQuery,
                        cancellationToken);

                return result.Match(
                    HttpResults.Ok,
                    ApiResults.Problem);
            });
    }
}
