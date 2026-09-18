using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Common.Presentation.ApiResults;
using StrmManager.Common.Presentation.Endpoints;
using StrmManager.Modules.Catalog.Application.Series.GetSeasons;
using ApiResults = StrmManager.Common.Presentation.ApiResults.ApiResults;
using HttpResults = Microsoft.AspNetCore.Http.Results;

namespace StrmManager.Modules.Catalog.Presentation.Series;

internal sealed class GetSeriesSeasons : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("api/series/{id:guid}/seasons", async (
            Guid id,
            IQueryHandler<GetSeriesSeasonsQuery, IReadOnlyList<SeasonResponse>> handler,
            CancellationToken cancellationToken) =>
        {
            Result<IReadOnlyList<SeasonResponse>> result = await handler.Handle(new GetSeriesSeasonsQuery(id), cancellationToken);

            return result.Match(HttpResults.Ok, ApiResults.Problem);
        });
    }
}
