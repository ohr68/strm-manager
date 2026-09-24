namespace StrmManager.Modules.Catalog.Application.Catalog.GetMovieDetail;

/// <summary>
/// The public API shape for UI-6b's read-only Movie Detail - metadata only, exactly the fields UI-6a's live
/// verification proved reliable (title/year/runtime/description/genres/rating/poster/backdrop). Deliberately no
/// provider/source URL, no ffprobe/processing information: this is purely what Cinemeta's own display metadata
/// says about the movie.
/// </summary>
public sealed record MovieDetailResponse(
    string ExternalId,
    string Title,
    int Year,
    int? RuntimeMinutes,
    string? Description,
    IReadOnlyList<string> Genres,
    string? ImdbRating,
    string? PosterUrl,
    string? BackdropUrl);
