using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Domain.Movies;

namespace StrmManager.Modules.Catalog.Application.Movies.GetMovie;

internal sealed class GetMovieQueryHandler(IMovieRepository movieRepository)
    : IQueryHandler<GetMovieQuery, MovieResponse>
{
    public async Task<Result<MovieResponse>> Handle(GetMovieQuery query, CancellationToken cancellationToken)
    {
        Movie? movie = await movieRepository.GetAsync(query.MovieId, cancellationToken);

        if (movie is null)
        {
            return Result.Failure<MovieResponse>(MovieErrors.NotFound(query.MovieId));
        }

        return MovieResponse.From(movie);
    }
}
