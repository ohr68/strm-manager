namespace StrmManager.Modules.MediaProcessing.Application.Streams.MovieIdentity;

/// <summary>
/// What is known about the movie being looked for - never the Catalog Movie entity. There is no
/// title on purpose: a title is never identity evidence. TmdbId is null when the catalog does
/// not know it (e.g. a movie added before Cinemeta's moviedb_id was mapped), in which case a
/// candidate's TMDB id can neither confirm nor contradict anything.
/// </summary>
public sealed record MovieIdentityReference(string ImdbId, string? TmdbId, int Year);
