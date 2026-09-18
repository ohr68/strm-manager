namespace StrmManager.Modules.Catalog.Application.Seasons.GetEpisodes;

public sealed record EpisodeResponse(
    Guid Id,
    int EpisodeNumber,
    string Title,
    string Status,
    DateTime ReleaseAtUtc,
    int AttemptCount,
    string? LastError);
