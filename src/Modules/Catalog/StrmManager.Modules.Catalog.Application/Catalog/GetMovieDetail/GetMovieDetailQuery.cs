using StrmManager.Common.Application.Messaging;

namespace StrmManager.Modules.Catalog.Application.Catalog.GetMovieDetail;

public sealed record GetMovieDetailQuery(string ImdbId) : IQuery<MovieDetailResponse>;
