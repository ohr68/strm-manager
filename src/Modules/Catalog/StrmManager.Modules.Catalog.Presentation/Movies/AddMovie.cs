using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Common.Presentation.ApiResults;
using StrmManager.Common.Presentation.Endpoints;
using StrmManager.Modules.Catalog.Application.Movies.AddMovie;
using ApiResults = StrmManager.Common.Presentation.ApiResults.ApiResults;
using HttpResults = Microsoft.AspNetCore.Http.Results;

namespace StrmManager.Modules.Catalog.Presentation.Movies;

internal sealed class AddMovie : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("api/movies", async (
            Request request,
            IValidator<AddMovieCommand> validator,
            ICommandHandler<AddMovieCommand, Guid> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new AddMovieCommand(request.ImdbId);

            Result<Guid> result = await handler.HandleValidated(command, validator, cancellationToken);

            return result.Match(
                id => HttpResults.Created($"/api/movies/{id}", new { id }),
                ApiResults.Problem);
        });
    }

    internal sealed class Request
    {
        public required string ImdbId { get; init; }
    }
}
