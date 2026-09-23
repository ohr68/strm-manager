using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Catalog.GetPopularMovies;
using StrmManager.Modules.Catalog.Application.Metadata;

namespace StrmManager.Modules.Catalog.Application.Catalog.GetMovieRows;

/// <summary>
/// UI-4's fixed movie rows (Popular/Action/Comedy/Drama/Horror). One ICatalogProvider call - the same call
/// GetPopularMoviesQueryHandler makes, just a larger source pool - then MovieRowPolicy buckets the single
/// already-fetched list into rows. No Movie aggregate is created or touched, no .strm is written, no provider/media
/// resolution occurs - catalog browsing is metadata-only, exactly like GetPopularMoviesQueryHandler.
/// </summary>
internal sealed class GetMovieRowsQueryHandler(ICatalogProvider catalogProvider)
    : IQueryHandler<GetMovieRowsQuery, MovieRowsResponse>
{
    // Cinemeta's "top" catalog already returns up to 50 items in ONE HTTP call regardless of this value (see
    // CinemetaCatalogProvider/CinemetaCatalogResponseDto - Take(limit) is purely client-side truncation of that
    // single already-fetched, already-proven response). Requesting more of what one existing call already returns
    // is not a new external capability or a second request - it just gives the genre buckets below more source
    // material than the Popular row's own (unchanged) 20-item limit would allow.
    private const int SourceLimit = 50;

    public async Task<Result<MovieRowsResponse>> Handle(GetMovieRowsQuery query, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<CatalogMovie>> result = await catalogProvider.GetPopularMoviesAsync(SourceLimit, cancellationToken);

        if (result.IsFailure)
        {
            return Result.Failure<MovieRowsResponse>(result.Error);
        }

        IReadOnlyList<MovieRow> rows = MovieRowPolicy.BuildRows(result.Value);

        List<MovieRowResponse> rowResponses = rows
            .Select(row => new MovieRowResponse(
                row.Id,
                row.Name,
                row.Movies
                    .Select(movie => new CatalogMovieResponse(movie.ExternalId, movie.Title, movie.Year, movie.PosterUrl))
                    .ToList()))
            .ToList();

        return new MovieRowsResponse(rowResponses);
    }
}
