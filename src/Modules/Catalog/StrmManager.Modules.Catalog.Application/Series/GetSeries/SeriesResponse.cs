namespace StrmManager.Modules.Catalog.Application.Series.GetSeries;

public sealed record SeriesResponse(
    Guid Id,
    string? ImdbId,
    string? TmdbId,
    string? TvdbId,
    string Title,
    string? OriginalTitle,
    int Year,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
