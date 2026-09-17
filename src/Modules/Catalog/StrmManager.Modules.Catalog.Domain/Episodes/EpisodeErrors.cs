using StrmManager.Common.Domain.Abstractions;

namespace StrmManager.Modules.Catalog.Domain.Episodes;

public static class EpisodeErrors
{
    public static Error NotFound(Guid episodeId) =>
        Error.NotFound("Episodes.NotFound", $"The episode with the identifier '{episodeId}' was not found.");
}
