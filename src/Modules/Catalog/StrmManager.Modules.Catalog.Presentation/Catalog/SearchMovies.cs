using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Application.Validation;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Common.Presentation.ApiResults;
using StrmManager.Common.Presentation.Endpoints;
using StrmManager.Modules.Catalog.Application.Catalog.SearchMovies;
using ApiResults = StrmManager.Common.Presentation.ApiResults.ApiResults;
using HttpResults = Microsoft.AspNetCore.Http.Results;

namespace StrmManager.Modules.Catalog.Presentation.Catalog;

/// <summary>
/// GET api/catalog/movies/search?query=...: free-text movie search (UI-5b), metadata-only like Popular/Rows. The
/// raw query string is trimmed here before validation/handling - required, and bounded to 100 characters after
/// trimming (never silently truncated). No matches is a successful 200 with an empty movies array, never a 404.
/// </summary>
internal sealed class SearchMovies : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("api/catalog/movies/search", async (
            string? query,
            IValidator<SearchMoviesQuery> validator,
            IQueryHandler<SearchMoviesQuery, SearchMoviesResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var searchQuery = new SearchMoviesQuery((query ?? string.Empty).Trim());

            ValidationResult validation = await validator.ValidateAsync(searchQuery, cancellationToken);

            if (!validation.IsValid)
            {
                return ApiResults.Problem(validation.ToApplicationResult());
            }

            Result<SearchMoviesResponse> result = await handler.Handle(searchQuery, cancellationToken);

            return result.Match(HttpResults.Ok, ApiResults.Problem);
        });
    }
}
