using StrmManager.Common.Application.Messaging;

namespace StrmManager.Modules.Catalog.Application.Catalog.GetPopularMovies;

public sealed record GetPopularMoviesQuery : IQuery<PopularMoviesResponse>;
