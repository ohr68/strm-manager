namespace StrmManager.Modules.MediaProcessing.Application.Streams;

/// <summary>
/// Provider-neutral stream candidate. Url is transient/sensitive data - it exists only
/// long enough to validate and write the .strm file; never logged, never persisted (see
/// SourceAttempt/StrmFile, which deliberately do not store it).
/// </summary>
public sealed record StreamCandidate(string Provider, string Name, string? Description, string Url);
