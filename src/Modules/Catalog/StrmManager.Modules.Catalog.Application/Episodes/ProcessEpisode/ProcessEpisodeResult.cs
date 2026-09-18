namespace StrmManager.Modules.Catalog.Application.Episodes.ProcessEpisode;

public sealed record SelectedSourceSummary(string Provider, string Name);

/// <summary>
/// One shape covers every terminal outcome (Completed/Unavailable/Error) - irrelevant
/// fields are simply null. StreamUrl is deliberately not a field here - never returned
/// to the API caller (see ADR-008/ADR-011 trust-boundary notes).
/// </summary>
public sealed record ProcessEpisodeResult(
    Guid EpisodeId,
    string Status,
    int Attempts,
    SelectedSourceSummary? SelectedSource,
    string? StrmPath,
    string? Reason);
