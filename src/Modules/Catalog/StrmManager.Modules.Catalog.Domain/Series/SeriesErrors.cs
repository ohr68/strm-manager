using StrmManager.Common.Domain.Abstractions;

namespace StrmManager.Modules.Catalog.Domain.Series;

public static class SeriesErrors
{
    public static Error NotFound(Guid seriesId) =>
        Error.NotFound("Series.NotFound", $"The series with the identifier '{seriesId}' was not found.");

    public static Error AlreadyExists(string imdbId) =>
        Error.Conflict("Series.AlreadyExists", $"A series with IMDb id '{imdbId}' already exists.");

    public static Error MissingImdbId(Guid seriesId) =>
        Error.Failure("Series.MissingImdbId", $"The series with the identifier '{seriesId}' has no IMDb id to refresh metadata with.");
}
