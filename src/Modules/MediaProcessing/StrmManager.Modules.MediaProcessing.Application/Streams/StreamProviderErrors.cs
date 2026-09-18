using StrmManager.Common.Domain.Abstractions;

namespace StrmManager.Modules.MediaProcessing.Application.Streams;

public static class StreamProviderErrors
{
    public static Error ProviderUnavailable(string provider) =>
        Error.Failure("Streams.ProviderUnavailable", $"{provider} is currently unavailable.");

    public static Error InvalidResponse(string provider, string reason) =>
        Error.Failure("Streams.InvalidResponse", $"{provider} returned an invalid response: {reason}");

    public static Error Timeout(string provider) =>
        Error.Failure("Streams.Timeout", $"{provider} did not respond in time.");
}
