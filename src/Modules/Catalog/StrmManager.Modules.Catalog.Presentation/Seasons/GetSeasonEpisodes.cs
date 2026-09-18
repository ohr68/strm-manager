using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Common.Presentation.ApiResults;
using StrmManager.Common.Presentation.Endpoints;
using StrmManager.Modules.Catalog.Application.Seasons.GetEpisodes;
using ApiResults = StrmManager.Common.Presentation.ApiResults.ApiResults;
using HttpResults = Microsoft.AspNetCore.Http.Results;

namespace StrmManager.Modules.Catalog.Presentation.Seasons;

internal sealed class GetSeasonEpisodes : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("api/seasons/{id:guid}/episodes", async (
            Guid id,
            IQueryHandler<GetSeasonEpisodesQuery, IReadOnlyList<EpisodeResponse>> handler,
            CancellationToken cancellationToken) =>
        {
            Result<IReadOnlyList<EpisodeResponse>> result = await handler.Handle(new GetSeasonEpisodesQuery(id), cancellationToken);

            return result.Match(HttpResults.Ok, ApiResults.Problem);
        });
    }
}
