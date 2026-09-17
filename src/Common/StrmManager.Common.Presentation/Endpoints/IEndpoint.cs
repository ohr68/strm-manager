using Microsoft.AspNetCore.Routing;

namespace StrmManager.Common.Presentation.Endpoints;

public interface IEndpoint
{
    void MapEndpoint(IEndpointRouteBuilder app);
}
