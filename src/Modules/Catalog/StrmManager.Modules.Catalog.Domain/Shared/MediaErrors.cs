using StrmManager.Common.Domain.Abstractions;

namespace StrmManager.Modules.Catalog.Domain.Shared;

public static class MediaErrors
{
    public static Error InvalidTransition(string aggregate, MediaStatus from, MediaStatus to) =>
        Error.Conflict(
            $"{aggregate}.InvalidTransition",
            $"Cannot transition {aggregate} from '{from}' to '{to}'.");
}
