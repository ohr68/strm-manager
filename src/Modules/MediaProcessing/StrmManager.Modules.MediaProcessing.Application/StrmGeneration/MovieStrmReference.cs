namespace StrmManager.Modules.MediaProcessing.Application.StrmGeneration;

/// <summary>
/// The catalog data needed to compute a Jellyfin-compatible movie .strm path - a neutral DTO for
/// the same reason as EpisodeStrmReference: IStrmWriter shouldn't need Catalog.Domain's Movie type.
/// The IMDb id goes into the folder name (Jellyfin's "[imdbid-tt...]" tag), which is what keeps two
/// different movies with the same title and year in separate folders.
/// </summary>
public sealed record MovieStrmReference(string Title, int Year, string ImdbId);
