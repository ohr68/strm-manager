namespace StrmManager.Modules.Catalog.Application.Metadata;

/// <summary>
/// Provider-neutral, DISPLAY-only movie metadata (UI-6b) - deliberately separate from MovieMetadata (which is what
/// AddMovie needs to create a Movie: title/year/runtime/release date, nothing else). MovieDetail exists purely for
/// browse-only rendering and must never be widened into or merged with MovieMetadata - a caller that wants to
/// create a Movie still goes through AddMovie/GetMovieAsync exactly as before, unaffected by this type.
/// </summary>
public sealed record MovieDetail(
    string ExternalId,
    string Title,
    int Year,
    TimeSpan? Runtime,
    string? Description,
    IReadOnlyList<string> Genres,
    string? ImdbRating,
    string? PosterUrl,
    string? BackdropUrl);
