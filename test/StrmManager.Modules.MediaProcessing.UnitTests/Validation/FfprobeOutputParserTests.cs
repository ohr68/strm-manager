using StrmManager.Modules.Catalog.Domain.SourceAttempts;
using StrmManager.Modules.MediaProcessing.Application.Validation;
using StrmManager.Modules.MediaProcessing.Infrastructure.Validation.Ffprobe;

namespace StrmManager.Modules.MediaProcessing.UnitTests.Validation;

/// <summary>
/// Pure JSON-in/MediaValidationResult-out tests - no ffprobe executable involved (see
/// FfprobeMediaValidatorTests for the process-invocation layer). Deterministic on every
/// machine, including the Windows dev box with no ffprobe installed.
/// </summary>
public class FfprobeOutputParserTests
{
    private static readonly FfprobeOptions Options = new()
    {
        EpisodeRuntimeTolerancePercentage = 35,
        MinimumEpisodeDurationSeconds = 1200,
    };

    private static string BuildOutput(double durationSeconds, bool includeVideo = true, bool includeAudio = true, string videoCodec = "h264")
    {
        List<string> streams = [];

        if (includeVideo)
        {
            streams.Add($$"""{ "codec_type": "video", "codec_name": "{{videoCodec}}" }""");
        }

        if (includeAudio)
        {
            streams.Add("""{ "codec_type": "audio", "codec_name": "aac" }""");
        }

        string durationText = durationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return $$"""
            {
                "streams": [ {{string.Join(", ", streams)}} ],
                "format": { "duration": "{{durationText}}" }
            }
            """;
    }

    [Fact]
    public void Parse_DurationWithinTolerance_LegacyValidatedExample_IsApproved()
    {
        // Expected 3120s, found 3003.88s -> ~3.72% off -> Approved (legacy-validated case).
        string json = BuildOutput(3003.88);

        MediaValidationResult result = FfprobeOutputParser.Parse(json, TimeSpan.FromSeconds(3120), Options);

        Assert.True(result.Approved);
        Assert.Equal(SourceAttemptResult.Approved, result.Result);
        Assert.InRange(result.DifferencePercentage!.Value, 3.7, 3.8);
    }

    [Fact]
    public void Parse_DurationAtEdgeOfTolerance_LegacyValidatedExample_IsApproved()
    {
        // Expected 3120s, found 2089.07s -> ~33.04% off -> still <= 35% -> Approved.
        string json = BuildOutput(2089.07);

        MediaValidationResult result = FfprobeOutputParser.Parse(json, TimeSpan.FromSeconds(3120), Options);

        Assert.True(result.Approved);
        Assert.InRange(result.DifferencePercentage!.Value, 33.0, 33.1);
    }

    [Fact]
    public void Parse_DurationBeyondTolerance_IsRejected()
    {
        // Expected 3120s, found 1000s -> ~67.9% off -> Rejected.
        string json = BuildOutput(1000);

        MediaValidationResult result = FfprobeOutputParser.Parse(json, TimeSpan.FromSeconds(3120), Options);

        Assert.False(result.Approved);
        Assert.Equal(SourceAttemptResult.Rejected, result.Result);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public void Parse_NoExpectedRuntime_DurationAboveMinimum_IsApproved()
    {
        string json = BuildOutput(1500);

        MediaValidationResult result = FfprobeOutputParser.Parse(json, expectedRuntime: null, Options);

        Assert.True(result.Approved);
        Assert.Null(result.ExpectedDuration);
        Assert.Null(result.DifferencePercentage);
    }

    [Fact]
    public void Parse_NoExpectedRuntime_DurationBelowMinimum_IsRejected()
    {
        string json = BuildOutput(900);

        MediaValidationResult result = FfprobeOutputParser.Parse(json, expectedRuntime: null, Options);

        Assert.False(result.Approved);
        Assert.Equal(SourceAttemptResult.Rejected, result.Result);
    }

    [Fact]
    public void Parse_MissingVideoStream_IsRejectedAsInvalidMedia()
    {
        string json = BuildOutput(1500, includeVideo: false);

        MediaValidationResult result = FfprobeOutputParser.Parse(json, TimeSpan.FromSeconds(1500), Options);

        Assert.False(result.Approved);
        Assert.Equal(SourceAttemptResult.InvalidMedia, result.Result);
        Assert.Contains("video", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_MissingAudioStream_IsRejectedAsInvalidMedia()
    {
        string json = BuildOutput(1500, includeAudio: false);

        MediaValidationResult result = FfprobeOutputParser.Parse(json, TimeSpan.FromSeconds(1500), Options);

        Assert.False(result.Approved);
        Assert.Equal(SourceAttemptResult.InvalidMedia, result.Result);
        Assert.Contains("audio", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("png")]
    [InlineData("mjpeg")]
    [InlineData("bmp")]
    public void Parse_ImageOnlyVideoStream_DoesNotCountAsValidVideo(string imageCodec)
    {
        // A "video" stream that's actually embedded cover art (e.g. an audio file with a
        // PNG thumbnail) must not satisfy the "has valid video" requirement.
        string json = BuildOutput(1500, videoCodec: imageCodec);

        MediaValidationResult result = FfprobeOutputParser.Parse(json, TimeSpan.FromSeconds(1500), Options);

        Assert.False(result.Approved);
        Assert.Equal(SourceAttemptResult.InvalidMedia, result.Result);
    }

    [Fact]
    public void Parse_MalformedJson_ReturnsProbeError()
    {
        MediaValidationResult result = FfprobeOutputParser.Parse("{ not valid json", TimeSpan.FromSeconds(1500), Options);

        Assert.False(result.Approved);
        Assert.Equal(SourceAttemptResult.ProbeError, result.Result);
    }

    [Fact]
    public void Parse_MissingDuration_ReturnsInvalidMedia()
    {
        string json = """{ "streams": [{ "codec_type": "video", "codec_name": "h264" }, { "codec_type": "audio", "codec_name": "aac" }], "format": {} }""";

        MediaValidationResult result = FfprobeOutputParser.Parse(json, TimeSpan.FromSeconds(1500), Options);

        Assert.False(result.Approved);
        Assert.Equal(SourceAttemptResult.InvalidMedia, result.Result);
    }
}
