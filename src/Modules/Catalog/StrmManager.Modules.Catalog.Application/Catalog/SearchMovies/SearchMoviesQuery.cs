using StrmManager.Common.Application.Messaging;

namespace StrmManager.Modules.Catalog.Application.Catalog.SearchMovies;

public sealed record SearchMoviesQuery(string Query) : IQuery<SearchMoviesResponse>;
