namespace StrmManager.Modules.Catalog.Application.Movies.GetMovie;

public sealed record MovieResponse(
    Guid Id,
    string? ImdbId,
    string? TmdbId,
    string? TvdbId,
    string Title,
    int Year,
    TimeSpan? Runtime,
    DateTime ReleaseAtUtc,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
