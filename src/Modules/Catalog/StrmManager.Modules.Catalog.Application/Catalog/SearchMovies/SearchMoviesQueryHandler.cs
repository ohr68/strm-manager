using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Catalog.GetPopularMovies;
using StrmManager.Modules.Catalog.Application.Metadata;

namespace StrmManager.Modules.Catalog.Application.Catalog.SearchMovies;

/// <summary>
/// UI-5b movie search. Pure read-through to ICatalogProvider - no Movie aggregate is created or touched, no .strm
/// is written, no provider/media resolution occurs, exactly like GetPopularMoviesQueryHandler. Query validation
/// (required, trimmed, max 100 chars) happens at the endpoint before this handler ever runs.
/// </summary>
internal sealed class SearchMoviesQueryHandler(ICatalogProvider catalogProvider)
    : IQueryHandler<SearchMoviesQuery, SearchMoviesResponse>
{
    private const int MovieLimit = 20;

    public async Task<Result<SearchMoviesResponse>> Handle(SearchMoviesQuery query, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<CatalogMovie>> result = await catalogProvider.SearchMoviesAsync(query.Query, MovieLimit, cancellationToken);

        if (result.IsFailure)
        {
            return Result.Failure<SearchMoviesResponse>(result.Error);
        }

        List<CatalogMovieResponse> movies = result.Value
            .Select(movie => new CatalogMovieResponse(movie.ExternalId, movie.Title, movie.Year, movie.PosterUrl))
            .ToList();

        return new SearchMoviesResponse(movies);
    }
}
