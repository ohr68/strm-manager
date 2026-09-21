using StrmManager.Common.Application.Messaging;
using StrmManager.Modules.Catalog.Application.Movies.GetMovie;

namespace StrmManager.Modules.Catalog.Application.Movies.GetMovieByImdbId;

/// <summary>
/// Looks a movie up by its IMDb id and returns the same <see cref="MovieResponse"/> as GetMovie. Purely observational: it never calls a
/// metadata or stream provider, never creates or changes a movie, and does not care what state the movie is in.
/// </summary>
public sealed record GetMovieByImdbIdQuery(string ImdbId) : IQuery<MovieResponse>;
