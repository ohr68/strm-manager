using StrmManager.Common.Domain.Abstractions;

namespace StrmManager.Modules.Catalog.Domain.Episodes;

public static class EpisodeErrors
{
    public static Error NotFound(Guid episodeId) =>
        Error.NotFound("Episodes.NotFound", $"The episode with the identifier '{episodeId}' was not found.");

    public static Error AlreadyBeingProcessed(Guid episodeId) =>
        Error.Conflict("Episodes.AlreadyBeingProcessed", $"The episode with the identifier '{episodeId}' is already being processed by another request.");
}
