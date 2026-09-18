using StrmManager.Common.Domain.Abstractions;

namespace StrmManager.Modules.Catalog.Application.Metadata;

/// <summary>
/// Distinguishes the ways a metadata provider call can fail as expected, non-exceptional
/// outcomes - a bug/technical failure still goes through GlobalExceptionHandler, never
/// through here.
/// </summary>
public static class MetadataProviderErrors
{
    public static Error SeriesNotFound(string provider, string externalId) =>
        Error.NotFound(
            "Metadata.SeriesNotFound",
            $"{provider} has no metadata for external id '{externalId}'.");

    public static Error ProviderUnavailable(string provider) =>
        Error.Failure("Metadata.ProviderUnavailable", $"{provider} is currently unavailable.");

    public static Error InvalidResponse(string provider, string reason) =>
        Error.Failure("Metadata.InvalidResponse", $"{provider} returned an invalid response: {reason}");

    public static Error Timeout(string provider) =>
        Error.Failure("Metadata.Timeout", $"{provider} did not respond in time.");
}
