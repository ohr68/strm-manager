using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Metadata;

namespace StrmManager.Modules.Catalog.Application.Catalog.GetMovieDetail;

/// <summary>
/// UI-6b's read-only Movie Detail. Pure read-through to IMetadataProvider.GetMovieDetailAsync - no Movie aggregate
/// is created or touched, no AddMovie/EnsureMovie/ProcessMovie call, no provider/media resolution, no ffprobe, no
/// STRM, no Jellyfin scan. Selecting/browsing a movie stays exactly as metadata-only as Popular/Rows/Search
/// already are; this handler simply looks a single movie up by id instead of listing many.
/// </summary>
internal sealed class GetMovieDetailQueryHandler(IMetadataProvider metadataProvider)
    : IQueryHandler<GetMovieDetailQuery, MovieDetailResponse>
{
    public async Task<Result<MovieDetailResponse>> Handle(GetMovieDetailQuery query, CancellationToken cancellationToken)
    {
        Result<MovieDetail> result = await metadataProvider.GetMovieDetailAsync(query.ImdbId, cancellationToken);

        if (result.IsFailure)
        {
            return Result.Failure<MovieDetailResponse>(result.Error);
        }

        MovieDetail detail = result.Value;

        return new MovieDetailResponse(
            detail.ExternalId,
            detail.Title,
            detail.Year,
            detail.Runtime is { } runtime ? (int)runtime.TotalMinutes : null,
            detail.Description,
            detail.Genres,
            detail.ImdbRating,
            detail.PosterUrl,
            detail.BackdropUrl);
    }
}
