using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Common.Presentation.ApiResults;
using StrmManager.Common.Presentation.Endpoints;
using StrmManager.Modules.Catalog.Application.Episodes.GetEpisodes;
using StrmManager.Modules.Catalog.Domain.Shared;
using ApiResults = StrmManager.Common.Presentation.ApiResults.ApiResults;
using HttpResults = Microsoft.AspNetCore.Http.Results;

namespace StrmManager.Modules.Catalog.Presentation.Episodes;

internal sealed class GetEpisodes : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("api/episodes", async (
            string? status,
            int page,
            int pageSize,
            IQueryHandler<GetEpisodesQuery, IReadOnlyList<EpisodeStatusSummary>> handler,
            CancellationToken cancellationToken) =>
        {
            MediaStatus? parsedStatus = Enum.TryParse(status, ignoreCase: true, out MediaStatus value) ? value : null;
            int effectivePage = page <= 0 ? 1 : page;
            int effectivePageSize = pageSize <= 0 ? 50 : pageSize;

            Result<IReadOnlyList<EpisodeStatusSummary>> result = await handler.Handle(
                new GetEpisodesQuery(parsedStatus, effectivePage, effectivePageSize), cancellationToken);

            return result.Match(HttpResults.Ok, ApiResults.Problem);
        });
    }
}
