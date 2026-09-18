using StrmManager.Common.Application.Messaging;

namespace StrmManager.Modules.Catalog.Application.Movies.GetMovie;

public sealed record GetMovieQuery(Guid MovieId) : IQuery<MovieResponse>;
