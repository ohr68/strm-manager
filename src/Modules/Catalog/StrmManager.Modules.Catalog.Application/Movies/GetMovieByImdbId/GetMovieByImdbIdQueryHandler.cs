using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Movies.GetMovie;
using StrmManager.Modules.Catalog.Domain.Movies;

namespace StrmManager.Modules.Catalog.Application.Movies.GetMovieByImdbId;

/// <summary>
/// Read-only: one indexed lookup (imdb_id is unique) through the existing IMovieRepository.GetByImdbIdAsync. It has no IUnitOfWork, no
/// provider and no clock, so it cannot save, call out or change state. The IMDb id has already been validated (ImdbIdRules) by the time it
/// gets here.
/// </summary>
internal sealed class GetMovieByImdbIdQueryHandler(IMovieRepository movieRepository)
    : IQueryHandler<GetMovieByImdbIdQuery, MovieResponse>
{
    public async Task<Result<MovieResponse>> Handle(GetMovieByImdbIdQuery query, CancellationToken cancellationToken)
    {
        Movie? movie = await movieRepository.GetByImdbIdAsync(query.ImdbId, cancellationToken);

        if (movie is null)
        {
            return Result.Failure<MovieResponse>(MovieErrors.NotFoundByImdbId(query.ImdbId));
        }

        return MovieResponse.From(movie);
    }
}
