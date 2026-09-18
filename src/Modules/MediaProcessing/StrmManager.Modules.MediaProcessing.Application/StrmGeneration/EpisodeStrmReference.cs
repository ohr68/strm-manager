namespace StrmManager.Modules.MediaProcessing.Application.StrmGeneration;

/// <summary>
/// The catalog data needed to compute a Jellyfin-compatible .strm path - not an external
/// provider concern, but kept as a neutral DTO for the same reason as the other
/// MediaProcessing contracts: IStrmWriter shouldn't need to know about Catalog.Domain's
/// Episode/Series entities.
/// </summary>
public sealed record EpisodeStrmReference(string SeriesTitle, int SeriesYear, int SeasonNumber, int EpisodeNumber);
