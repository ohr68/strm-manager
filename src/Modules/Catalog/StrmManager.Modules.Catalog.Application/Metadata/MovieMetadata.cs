using StrmManager.Modules.Catalog.Domain.Shared;

namespace StrmManager.Modules.Catalog.Application.Metadata;

/// <summary>
/// Provider-neutral movie metadata - what AddMovie needs, not everything a given
/// provider's API happens to return. Provider-specific DTOs never cross this boundary
/// (see IMetadataProvider). ReleaseAtUtc is nullable because a provider can legitimately
/// have no reliable release date - callers must not invent one (AddMovie rejects the
/// movie instead of scheduling it with a fake date).
/// </summary>
public sealed record MovieMetadata(
    ExternalIds ExternalIds,
    string Title,
    int Year,
    TimeSpan? Runtime,
    DateTime? ReleaseAtUtc);
