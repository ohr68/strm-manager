using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Metadata;

namespace StrmManager.Modules.Catalog.Application.Catalog.GetPopularMovies;

/// <summary>
/// UI-2's "Popular Movies" row. Pure read-through to ICatalogProvider - no Movie aggregate is created or touched,
/// no .strm is written, no provider/media resolution occurs. Catalog browsing is metadata-only (see the UI-2
/// report); a card becoming a real Movie is a later slice's job (EnsureMovie/ProcessMovie), not this one's.
/// </summary>
internal sealed class GetPopularMoviesQueryHandler(ICatalogProvider catalogProvider)
    : IQueryHandler<GetPopularMoviesQuery, PopularMoviesResponse>
{
    private const int MovieLimit = 20;

    public async Task<Result<PopularMoviesResponse>> Handle(GetPopularMoviesQuery query, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<CatalogMovie>> result = await catalogProvider.GetPopularMoviesAsync(MovieLimit, cancellationToken);

        if (result.IsFailure)
        {
            return Result.Failure<PopularMoviesResponse>(result.Error);
        }

        List<CatalogMovieResponse> movies = result.Value
            .Select(movie => new CatalogMovieResponse(movie.ExternalId, movie.Title, movie.Year, movie.PosterUrl))
            .ToList();

        return new PopularMoviesResponse(movies);
    }
}
