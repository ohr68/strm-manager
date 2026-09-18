namespace StrmManager.Modules.Catalog.Application.Movies.ProcessMovie;

/// <summary>
/// One shape for every outcome a process request can report - irrelevant fields are
/// simply null. Deliberately Movie's own record rather than a shared one with
/// ProcessEpisodeResult: the two identify different aggregates and will not necessarily
/// evolve together. No stream URL is, or ever becomes, a field here (ADR-008/ADR-011
/// trust-boundary notes).
/// </summary>
public sealed record ProcessMovieResult(
    Guid MovieId,
    string Status,
    int Attempts,
    string? StrmPath,
    string? Reason);
