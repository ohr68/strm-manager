using Microsoft.Extensions.Options;
using StrmManager.Modules.Catalog.Domain.SourceAttempts;
using StrmManager.Modules.MediaProcessing.Application.Streams;
using StrmManager.Modules.MediaProcessing.Application.Validation;
using StrmManager.Modules.MediaProcessing.Infrastructure.Validation.Ffprobe;

namespace StrmManager.Modules.MediaProcessing.UnitTests.Validation;

/// <summary>
/// Security regression: a provider media URL must never reach the application log, whichever way
/// ffprobe fails. ffprobe receives the URL as an argument and echoes it in its own error output, so
/// anything that logs ffprobe's stderr/stdout (or a message built from it) leaks the signed URL.
/// These tests assert on what the ILogger actually received, not only on the returned result.
/// </summary>
public class FfprobeMediaValidatorLoggingTests
{
    // Unique, sensitive-looking parts. No cmd/sh metacharacters, so the fake script can print it verbatim.
    private const string Host = "canary-host-7f3a91c2.invalid";
    private const string PathToken = "PATHSECRET-5d21b8";
    private const string QueryToken = "QUERYSECRET-8e42f0";
    private const string CanaryUrl = $"https://{Host}/hls/{PathToken}/master.m3u8?sig={QueryToken}";

    private static readonly string[] CanaryParts = [CanaryUrl, Host, PathToken, QueryToken];

    private static readonly MediaValidationReference Reference = new(TimeSpan.FromSeconds(1500));
    private static readonly StreamCandidate Candidate = new("FrostStream", "Test Source", null, CanaryUrl);

    private static FfprobeMediaValidator CreateValidator(string executablePath, CapturingLogger<FfprobeMediaValidator> logger, int timeoutSeconds = 5) =>
        new(Options.Create(new FfprobeOptions { ExecutablePath = executablePath, TimeoutSeconds = timeoutSeconds }), logger);

    private static void AssertNoCanary(CapturingLogger<FfprobeMediaValidator> logger)
    {
        Assert.NotEmpty(logger.Entries); // the failure must still be logged, just without the URL

        foreach (string part in CanaryParts)
        {
            Assert.DoesNotContain(part, logger.AllText, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ValidateAsync_NonZeroExitWithUrlOnStderr_DoesNotLogTheUrl()
    {
        using FakeFfprobeExecutable ffprobe = FakeFfprobeExecutable.Emitting(
            stderr: $"{CanaryUrl}: Server returned 403 Forbidden (access denied)", stdout: string.Empty, exitCode: 1);
        var logger = new CapturingLogger<FfprobeMediaValidator>();

        MediaValidationResult result = await CreateValidator(ffprobe.Path, logger).ValidateAsync(Candidate, Reference);

        Assert.False(result.Approved);
        Assert.Equal(SourceAttemptResult.ProbeError, result.Result);
        Assert.Equal("ffprobe exited with code 1.", result.FailureReason);
        AssertNoCanary(logger);
    }

    [Fact]
    public async Task ValidateAsync_NonZeroExitWithUrlOnStdoutAndStderr_DoesNotLogTheUrl()
    {
        using FakeFfprobeExecutable ffprobe = FakeFfprobeExecutable.Emitting(
            stderr: $"Error opening input {CanaryUrl}", stdout: $"partial output for {CanaryUrl}", exitCode: 2);
        var logger = new CapturingLogger<FfprobeMediaValidator>();

        MediaValidationResult result = await CreateValidator(ffprobe.Path, logger).ValidateAsync(Candidate, Reference);

        Assert.False(result.Approved);
        Assert.Equal("ffprobe exited with code 2.", result.FailureReason);
        AssertNoCanary(logger);
    }

    [Fact]
    public async Task ValidateAsync_NonZeroExit_StillLogsTheExitCodeAndACategory()
    {
        using FakeFfprobeExecutable ffprobe = FakeFfprobeExecutable.Emitting(
            stderr: $"{CanaryUrl}: Server returned 403 Forbidden (access denied)", stdout: string.Empty, exitCode: 1);
        var logger = new CapturingLogger<FfprobeMediaValidator>();

        await CreateValidator(ffprobe.Path, logger).ValidateAsync(Candidate, Reference);

        string log = logger.AllText;
        Assert.Contains("ExitCode=1", log);
        Assert.Contains("FailureCategory=HttpForbidden", log);
        Assert.Contains("Test Source", log); // the candidate label, not the URL
    }

    [Theory]
    [InlineData("Server returned 403 Forbidden (access denied)", "HttpForbidden")]
    [InlineData("Server returned 404 Not Found", "HttpNotFound")]
    [InlineData("Server returned 5XX Server Error reply", "HttpServerError")]
    [InlineData("Connection refused", "ConnectionFailed")]
    [InlineData("Failed to resolve hostname", "ConnectionFailed")]
    [InlineData("Connection timed out", "Timeout")]
    [InlineData("Invalid data found when processing input", "InvalidData")]
    [InlineData("something ffprobe has never said", "Unknown")]
    public async Task ValidateAsync_StderrIsClassifiedIntoAFixedCategory(string ffprobeMessage, string expectedCategory)
    {
        using FakeFfprobeExecutable ffprobe = FakeFfprobeExecutable.Emitting(
            stderr: $"{CanaryUrl}: {ffprobeMessage}", stdout: string.Empty, exitCode: 1);
        var logger = new CapturingLogger<FfprobeMediaValidator>();

        await CreateValidator(ffprobe.Path, logger).ValidateAsync(Candidate, Reference);

        Assert.Contains($"FailureCategory={expectedCategory}", logger.AllText);
        AssertNoCanary(logger);
    }

    [Fact]
    public async Task ValidateAsync_Timeout_DoesNotLogTheUrl()
    {
        using FakeFfprobeExecutable ffprobe = FakeFfprobeExecutable.Hanging(stderr: $"Opening {CanaryUrl}");
        var logger = new CapturingLogger<FfprobeMediaValidator>();

        MediaValidationResult result = await CreateValidator(ffprobe.Path, logger, timeoutSeconds: 1).ValidateAsync(Candidate, Reference);

        Assert.False(result.Approved);
        Assert.Equal(SourceAttemptResult.Timeout, result.Result);
        Assert.Equal("ffprobe did not finish within the configured timeout.", result.FailureReason);
        AssertNoCanary(logger);
    }

    [Fact]
    public async Task ValidateAsync_ExitZeroWithMalformedOutputContainingTheUrl_DoesNotLogTheUrl()
    {
        using FakeFfprobeExecutable ffprobe = FakeFfprobeExecutable.Emitting(
            stderr: $"warning about {CanaryUrl}", stdout: $"this is not json {CanaryUrl}", exitCode: 0);
        var logger = new CapturingLogger<FfprobeMediaValidator>();

        MediaValidationResult result = await CreateValidator(ffprobe.Path, logger).ValidateAsync(Candidate, Reference);

        Assert.False(result.Approved);
        Assert.Equal(SourceAttemptResult.ProbeError, result.Result);
        Assert.Equal("ffprobe output could not be parsed as JSON.", result.FailureReason);
        Assert.NotEmpty(logger.Entries);

        foreach (string part in CanaryParts)
        {
            Assert.DoesNotContain(part, logger.AllText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(part, result.FailureReason, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ValidateAsync_ExecutableFailsToStart_DoesNotLogTheUrl()
    {
        var logger = new CapturingLogger<FfprobeMediaValidator>();

        MediaValidationResult result = await CreateValidator("strm-manager-nonexistent-ffprobe-binary", logger).ValidateAsync(Candidate, Reference);

        Assert.False(result.Approved);
        Assert.Equal("ffprobe executable is not available.", result.FailureReason);
        AssertNoCanary(logger);
    }

    [Fact]
    public async Task ValidateAsync_RejectedScheme_DoesNotLogTheUrl()
    {
        var logger = new CapturingLogger<FfprobeMediaValidator>();
        var candidate = new StreamCandidate("FrostStream", "Test Source", null, $"ftp://{Host}/{PathToken}?sig={QueryToken}");

        MediaValidationResult result = await CreateValidator("strm-manager-nonexistent-ffprobe-binary", logger).ValidateAsync(candidate, Reference);

        Assert.False(result.Approved);
        AssertNoCanary(logger);
    }
}
