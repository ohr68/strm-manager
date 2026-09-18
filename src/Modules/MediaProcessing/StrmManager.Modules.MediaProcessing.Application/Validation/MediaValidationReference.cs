namespace StrmManager.Modules.MediaProcessing.Application.Validation;

/// <summary>
/// ExpectedRuntime drives the tolerance-percentage rule when available, and is null when
/// unreliable/unknown - callers must never invent one (see the minimum-duration fallback
/// rule instead).
/// </summary>
public sealed record MediaValidationReference(TimeSpan? ExpectedRuntime);
