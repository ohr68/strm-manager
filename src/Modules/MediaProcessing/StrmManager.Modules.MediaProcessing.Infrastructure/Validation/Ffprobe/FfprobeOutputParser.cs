using System.Globalization;
using System.Text.Json;
using StrmManager.Modules.Catalog.Domain.SourceAttempts;
using StrmManager.Modules.MediaProcessing.Application.Validation;

namespace StrmManager.Modules.MediaProcessing.Infrastructure.Validation.Ffprobe;

/// <summary>
/// Pure JSON-in, MediaValidationResult-out logic - deliberately separate from process
/// invocation (FfprobeMediaValidator) so it can be unit tested with fixture JSON, with no
/// ffprobe executable required (see ADR-009). Applies the two duration rules: percentage
/// tolerance when ExpectedRuntime is known, minimum-duration floor otherwise - never both,
/// never invented.
/// </summary>
internal static class FfprobeOutputParser
{
    private static readonly HashSet<string> ImageOnlyVideoCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "png", "mjpeg", "bmp", "gif", "tiff",
    };

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static MediaValidationResult Parse(string ffprobeJson, TimeSpan? expectedRuntime, FfprobeOptions options)
    {
        FfprobeOutputDto? output;

        try
        {
            output = JsonSerializer.Deserialize<FfprobeOutputDto>(ffprobeJson, SerializerOptions);
        }
        catch (JsonException)
        {
            return MediaValidationResult.ForRejection(SourceAttemptResult.ProbeError, "ffprobe output could not be parsed as JSON.");
        }

        if (output is null)
        {
            return MediaValidationResult.ForRejection(SourceAttemptResult.ProbeError, "ffprobe produced no output.");
        }

        List<FfprobeStreamDto> streams = output.Streams ?? [];

        bool hasValidVideo = streams.Any(stream =>
            string.Equals(stream.CodecType, "video", StringComparison.OrdinalIgnoreCase) &&
            !ImageOnlyVideoCodecs.Contains(stream.CodecName ?? string.Empty));

        bool hasValidAudio = streams.Any(stream =>
            string.Equals(stream.CodecType, "audio", StringComparison.OrdinalIgnoreCase));

        string? videoCodec = streams.FirstOrDefault(stream =>
            string.Equals(stream.CodecType, "video", StringComparison.OrdinalIgnoreCase))?.CodecName;
        string? audioCodec = streams.FirstOrDefault(stream =>
            string.Equals(stream.CodecType, "audio", StringComparison.OrdinalIgnoreCase))?.CodecName;

        if (!hasValidVideo)
        {
            return MediaValidationResult.ForRejection(
                SourceAttemptResult.InvalidMedia, "No valid video stream (image-only attachments such as cover art do not count).",
                videoCodec: videoCodec, audioCodec: audioCodec);
        }

        if (!hasValidAudio)
        {
            return MediaValidationResult.ForRejection(
                SourceAttemptResult.InvalidMedia, "No audio stream found.", videoCodec: videoCodec, audioCodec: audioCodec);
        }

        if (!TryParseDuration(output.Format?.Duration, out TimeSpan duration))
        {
            return MediaValidationResult.ForRejection(
                SourceAttemptResult.InvalidMedia, "Media has no readable duration.", videoCodec: videoCodec, audioCodec: audioCodec);
        }

        return EvaluateDuration(duration, expectedRuntime, videoCodec, audioCodec, options);
    }

    private static MediaValidationResult EvaluateDuration(
        TimeSpan duration,
        TimeSpan? expectedRuntime,
        string? videoCodec,
        string? audioCodec,
        FfprobeOptions options)
    {
        if (expectedRuntime is { } expected && expected > TimeSpan.Zero)
        {
            double differencePercentage = Math.Abs((duration - expected).TotalSeconds) / expected.TotalSeconds * 100.0;

            if (differencePercentage <= options.EpisodeRuntimeTolerancePercentage)
            {
                return MediaValidationResult.ForApproval(duration, expected, differencePercentage, videoCodec, audioCodec);
            }

            return MediaValidationResult.ForRejection(
                SourceAttemptResult.Rejected,
                $"Duration {duration} differs from expected {expected} by {differencePercentage:F2}% (tolerance {options.EpisodeRuntimeTolerancePercentage}%).",
                duration, expected, differencePercentage, videoCodec, audioCodec);
        }

        // No reliable expected runtime - fall back to the minimum-duration safety rule
        // rather than inventing an expected value.
        if (duration.TotalSeconds >= options.MinimumEpisodeDurationSeconds)
        {
            return MediaValidationResult.ForApproval(duration, null, null, videoCodec, audioCodec);
        }

        return MediaValidationResult.ForRejection(
            SourceAttemptResult.Rejected,
            $"Duration {duration} is below the {options.MinimumEpisodeDurationSeconds}s minimum safety threshold (no expected runtime available).",
            duration, null, null, videoCodec, audioCodec);
    }

    private static bool TryParseDuration(string? rawDuration, out TimeSpan duration)
    {
        if (!string.IsNullOrWhiteSpace(rawDuration) &&
            double.TryParse(rawDuration, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) &&
            seconds > 0)
        {
            duration = TimeSpan.FromSeconds(seconds);
            return true;
        }

        duration = default;
        return false;
    }
}
