using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StrmManager.Modules.Catalog.Domain.SourceAttempts;
using StrmManager.Modules.MediaProcessing.Application.Streams;
using StrmManager.Modules.MediaProcessing.Application.Validation;
using StrmManager.Modules.MediaProcessing.Infrastructure.Validation.Ffprobe;

namespace StrmManager.Modules.MediaProcessing.UnitTests.Validation;

/// <summary>
/// Process-invocation-layer tests only - the parsing/tolerance rules are covered
/// exhaustively, and independently of any executable, in FfprobeOutputParserTests. These
/// tests intentionally never require ffprobe to actually be installed (the Windows dev
/// machine doesn't have it) - they only exercise the failure paths a missing/misbehaving
/// executable produces, which are deterministic regardless of environment.
/// </summary>
public class FfprobeMediaValidatorTests
{
    private static FfprobeMediaValidator CreateValidator(string executablePath) =>
        new(Options.Create(new FfprobeOptions { ExecutablePath = executablePath, TimeoutSeconds = 5 }), NullLogger<FfprobeMediaValidator>.Instance);

    [Fact]
    public async Task ValidateAsync_ExecutableDoesNotExist_ReturnsProbeErrorWithoutThrowing()
    {
        FfprobeMediaValidator validator = CreateValidator("strm-manager-nonexistent-ffprobe-binary");
        var candidate = new StreamCandidate("FrostStream", "Test Source", null, "https://media.example.test/video");

        MediaValidationResult result = await validator.ValidateAsync(candidate, new MediaValidationReference(TimeSpan.FromSeconds(1500)));

        Assert.False(result.Approved);
        Assert.Equal(SourceAttemptResult.ProbeError, result.Result);
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://media.example.test/video")]
    public async Task ValidateAsync_NonHttpUrl_IsRejectedWithoutInvokingFfprobe(string url)
    {
        // ExecutablePath deliberately points at a nonexistent binary - if the scheme
        // check didn't short-circuit first, this would still fail, but as ProbeError
        // (execution failure) rather than the scheme-specific rejection we assert here,
        // proving the URL never reached the process-start step.
        FfprobeMediaValidator validator = CreateValidator("strm-manager-nonexistent-ffprobe-binary");
        var candidate = new StreamCandidate("FrostStream", "Test Source", null, url);

        MediaValidationResult result = await validator.ValidateAsync(candidate, new MediaValidationReference(TimeSpan.FromSeconds(1500)));

        Assert.False(result.Approved);
        Assert.Equal(SourceAttemptResult.ProbeError, result.Result);
        Assert.Contains("http", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }
}
