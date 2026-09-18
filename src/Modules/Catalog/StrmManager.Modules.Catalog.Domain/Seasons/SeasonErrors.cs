using StrmManager.Common.Domain.Abstractions;

namespace StrmManager.Modules.Catalog.Domain.Seasons;

public static class SeasonErrors
{
    public static Error NotFound(Guid seasonId) =>
        Error.NotFound("Seasons.NotFound", $"The season with the identifier '{seasonId}' was not found.");
}
