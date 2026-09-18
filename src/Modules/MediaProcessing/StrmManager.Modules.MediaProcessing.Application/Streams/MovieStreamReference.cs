namespace StrmManager.Modules.MediaProcessing.Application.Streams;

/// <summary>
/// The minimum movie data a stream provider needs - never the full Catalog Movie entity.
/// Keeps MediaProcessing.Application ignorant of Catalog.Domain's Movie type, same as
/// EpisodeStreamReference does for episodes.
/// </summary>
public sealed record MovieStreamReference(
    string ImdbId,
    string Title,
    int Year,
    TimeSpan? ExpectedRuntime);
