namespace StrmManager.Modules.Catalog.Application.Movies.ProcessMovie;

/// <summary>Which candidate passed validation - provider and display name only, never its URL.</summary>
public sealed record SelectedMovieSource(string Provider, string Name);

/// <summary>
/// One shape for every outcome a process request can report - irrelevant fields are
/// simply null. Deliberately Movie's own record rather than a shared one with
/// ProcessEpisodeResult: the two identify different aggregates and will not necessarily
/// evolve together. Attempts is how many candidates were evaluated in this run (the
/// movie's recorded attempt count when it was already Completed), exactly as for Episode.
/// No stream URL is, or ever becomes, a field here (ADR-008/ADR-011 trust-boundary notes).
/// </summary>
public sealed record ProcessMovieResult(
    Guid MovieId,
    string Status,
    int Attempts,
    SelectedMovieSource? SelectedSource,
    string? StrmPath,
    string? Reason);
