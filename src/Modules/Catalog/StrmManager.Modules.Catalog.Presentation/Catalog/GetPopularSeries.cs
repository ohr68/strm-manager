using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Common.Presentation.ApiResults;
using StrmManager.Common.Presentation.Endpoints;
using StrmManager.Modules.Catalog.Application.Catalog.GetPopularSeries;
using ApiResults =
    StrmManager.Common.Presentation.ApiResults.ApiResults;
using HttpResults =
    Microsoft.AspNetCore.Http.Results;

namespace StrmManager.Modules.Catalog.Presentation.Catalog;

internal sealed class GetPopularSeries : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet(
            "api/catalog/series/popular",
            async (
                IQueryHandler<
                    GetPopularSeriesQuery,
                    PopularSeriesResponse> handler,
                CancellationToken cancellationToken) =>
            {
                Result<PopularSeriesResponse> result =
                    await handler.Handle(
                        new GetPopularSeriesQuery(),
                        cancellationToken);

                return result.Match(
                    HttpResults.Ok,
                    ApiResults.Problem);
            });
    }
}
