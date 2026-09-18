using StrmManager.Modules.Catalog.Domain.SourceAttempts;
using StrmManager.Modules.MediaProcessing.Application.Streams;
using StrmManager.Modules.MediaProcessing.Application.Validation;

namespace StrmManager.Modules.Catalog.IntegrationTests.Processing;

/// <summary>Builders for provider candidates and validator answers used by the ProcessMovie tests.</summary>
internal static class MovieCandidates
{
    public const string SecretMarker = "SECRET-TOKEN";

    public static readonly MediaValidationResult Approval =
        MediaValidationResult.ForApproval(TimeSpan.FromMinutes(101), TimeSpan.FromMinutes(100), 1.0, "h264", "aac");

    public static MediaValidationResult Rejection(string reason = "Duration differs from the expected runtime.") =>
        MediaValidationResult.ForRejection(SourceAttemptResult.Rejected, reason);

    /// <summary>The URL a candidate carries - fake, but shaped like a signed one, and always containing <see cref="SecretMarker"/>.</summary>
    public static string UrlFor(string name) =>
        $"https://media.example.test/{name.Replace(' ', '-')}?token={SecretMarker}-{name.Replace(' ', '-')}";

    /// <summary>
    /// A FrostStream-shaped candidate: description "🎬 {title}[ (year)]" then a source and a language line.
    /// Pass year: null for the title-only shape the real provider serves for several movies at once.
    /// </summary>
    public static StreamCandidate Candidate(
        string name,
        int? year = 2025,
        string title = "Titulo Local",
        string? tmdbId = null,
        bool requiresCustomHeaders = false,
        string? extraDescription = null) =>
        new(
            "FrostStream",
            name,
            $"🎬 {title}{(year is { } y ? $" ({y})" : string.Empty)}\n🌊 Space\n🌎 Português{(extraDescription is null ? string.Empty : "\n" + extraDescription)}",
            UrlFor(name),
            tmdbId,
            requiresCustomHeaders);
}
