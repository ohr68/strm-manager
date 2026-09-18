namespace StrmManager.Modules.Catalog.Application.Episodes.GetEpisodes;

public sealed record EpisodeStatusSummary(
    Guid Id,
    Guid SeasonId,
    int SeasonNumber,
    int EpisodeNumber,
    string Title,
    string Status,
    DateTime ReleaseAtUtc,
    int AttemptCount,
    DateTime? NextAttemptAtUtc,
    string? LastError);
