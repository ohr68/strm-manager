using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Application.Validation;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Common.Presentation.ApiResults;
using StrmManager.Common.Presentation.Endpoints;
using StrmManager.Modules.Catalog.Application.Series.AddSeries;
using ApiResults = StrmManager.Common.Presentation.ApiResults.ApiResults;
using HttpResults = Microsoft.AspNetCore.Http.Results;

namespace StrmManager.Modules.Catalog.Presentation.Series;

internal sealed class AddSeries : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("api/series", async (
            Request request,
            IValidator<AddSeriesCommand> validator,
            ICommandHandler<AddSeriesCommand, Guid> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new AddSeriesCommand(
                request.ImdbId,
                request.TmdbId,
                request.TvdbId,
                request.Title,
                request.OriginalTitle,
                request.Year);

            FluentValidation.Results.ValidationResult validationResult = await validator.ValidateAsync(command, cancellationToken);

            if (!validationResult.IsValid)
            {
                return ApiResults.Problem(validationResult.ToApplicationResult());
            }

            Result<Guid> result = await handler.Handle(command, cancellationToken);

            return result.Match(
                id => HttpResults.Created($"/api/series/{id}", new { id }),
                ApiResults.Problem);
        });
    }

    internal sealed class Request
    {
        public required string ImdbId { get; init; }

        public string? TmdbId { get; init; }

        public string? TvdbId { get; init; }

        public required string Title { get; init; }

        public string? OriginalTitle { get; init; }

        public required int Year { get; init; }
    }
}
