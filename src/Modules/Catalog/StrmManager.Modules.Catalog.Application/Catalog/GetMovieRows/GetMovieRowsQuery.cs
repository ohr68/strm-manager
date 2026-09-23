using StrmManager.Common.Application.Messaging;

namespace StrmManager.Modules.Catalog.Application.Catalog.GetMovieRows;

public sealed record GetMovieRowsQuery : IQuery<MovieRowsResponse>;
