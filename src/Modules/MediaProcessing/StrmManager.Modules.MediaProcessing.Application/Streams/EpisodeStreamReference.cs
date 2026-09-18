namespace StrmManager.Modules.MediaProcessing.Application.Streams;

/// <summary>
/// The minimum episode data a stream provider needs - never the full Catalog Episode
/// entity. Keeps MediaProcessing.Application ignorant of Catalog.Domain's Episode type.
/// </summary>
public sealed record EpisodeStreamReference(
    string ExternalId,
    int SeasonNumber,
    int EpisodeNumber,
    string Title,
    TimeSpan? ExpectedRuntime);
