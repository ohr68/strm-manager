namespace StrmManager.Modules.Catalog.Application.Metadata;

/// <summary>
/// Provider-neutral episode metadata. ReleaseAtUtc is nullable because a provider can
/// legitimately have no reliable release date for an episode yet - callers must not
/// invent one (see CatalogSynchronizer, which skips episodes without a release date
/// rather than defaulting to "now").
/// </summary>
public sealed record EpisodeMetadata(
    string ExternalId,
    string Title,
    int SeasonNumber,
    int EpisodeNumber,
    TimeSpan? Runtime,
    DateTime? ReleaseAtUtc);
