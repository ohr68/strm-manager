using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StrmManager.Modules.Catalog.Domain.SourceAttempts;
using StrmManager.Modules.MediaProcessing.Application.Streams;
using StrmManager.Modules.MediaProcessing.Application.Validation;

namespace StrmManager.Modules.MediaProcessing.Infrastructure.Validation.Ffprobe;

/// <summary>
/// Runs ffprobe as a plain child process (ProcessStartInfo + ArgumentList, no shell) to
/// extract duration/stream codecs, then delegates to FfprobeOutputParser for the
/// rule evaluation. See ADR-009 for the process-safety and trust-boundary reasoning.
/// </summary>
internal sealed partial class FfprobeMediaValidator(IOptions<FfprobeOptions> options, ILogger<FfprobeMediaValidator> logger)
    : IMediaValidator
{
    private const int MaxCapturedOutputBytes = 4 * 1024 * 1024; // bounded output handling

    public async Task<MediaValidationResult> ValidateAsync(
        StreamCandidate candidate,
        MediaValidationReference reference,
        CancellationToken cancellationToken = default)
    {
        FfprobeOptions ffprobeOptions = options.Value;

        // Trust boundary: candidate.Url comes from an external provider. Only http/https
        // may ever reach the ffprobe argument list - never a local path or another scheme.
        if (!Uri.TryCreate(candidate.Url, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            LogRejectedScheme(logger, candidate.Name);
            return MediaValidationResult.ForRejection(SourceAttemptResult.ProbeError, "Candidate URL is not http/https.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = ffprobeOptions.ExecutablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-print_format");
        startInfo.ArgumentList.Add("json");
        startInfo.ArgumentList.Add("-show_format");
        startInfo.ArgumentList.Add("-show_streams");
        startInfo.ArgumentList.Add(candidate.Url);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(ffprobeOptions.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        Stopwatch stopwatch = Stopwatch.StartNew();
        Process? process = null;

        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            LogExecutableUnavailable(logger, ffprobeOptions.ExecutablePath, exception.Message);
            return MediaValidationResult.ForRejection(SourceAttemptResult.ProbeError, "ffprobe executable is not available.");
        }

        if (process is null)
        {
            LogExecutableUnavailable(logger, ffprobeOptions.ExecutablePath, "Process.Start returned null.");
            return MediaValidationResult.ForRejection(SourceAttemptResult.ProbeError, "ffprobe executable is not available.");
        }

        using (process)
        {
            Task<string> stdOutTask = ReadBoundedAsync(process.StandardOutput, linkedCts.Token);
            Task<string> stdErrTask = ReadBoundedAsync(process.StandardError, linkedCts.Token);

            try
            {
                await process.WaitForExitAsync(linkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                TryKillProcessTree(process, candidate.Name);

                if (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    LogProbeTimeout(logger, candidate.Name, stopwatch.ElapsedMilliseconds);
                    return MediaValidationResult.ForRejection(SourceAttemptResult.Timeout, "ffprobe did not finish within the configured timeout.");
                }

                throw;
            }

            string standardOutput = await stdOutTask;
            string standardError = await stdErrTask;

            if (process.ExitCode != 0)
            {
                // ffprobe echoes its input URL (a signed provider URL) in its error output, so neither
                // stderr nor stdout may reach a log or a message. Only the exit code and a category
                // chosen from a fixed set - never a slice of ffprobe's text - are recorded.
                LogProbeFailed(logger, candidate.Name, process.ExitCode, ClassifyFailure(standardError));
                return MediaValidationResult.ForRejection(SourceAttemptResult.ProbeError, $"ffprobe exited with code {process.ExitCode}.");
            }

            MediaValidationResult result = FfprobeOutputParser.Parse(standardOutput, reference.ExpectedRuntime, ffprobeOptions);

            LogProbeCompleted(logger, candidate.Name, result.Approved, result.Result, stopwatch.ElapsedMilliseconds);

            return result;
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new StringBuilder();
        char[] chunk = new char[8192];

        while (buffer.Length < MaxCapturedOutputBytes)
        {
            int read = await reader.ReadAsync(chunk, cancellationToken);

            if (read == 0)
            {
                break;
            }

            buffer.Append(chunk, 0, read);
        }

        return buffer.ToString();
    }

    private static void TryKillProcessTree(Process process, string candidateName)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Process already exited between the check and the kill attempt - fine.
        }
    }

    /// <summary>
    /// Maps ffprobe's stderr to a fixed category by well-known phrases. The input is only inspected, never
    /// returned, so the result can be logged without carrying any of the text (or URL) it came from. Phrases
    /// rather than bare status numbers are matched so a number inside the echoed URL cannot pick a category.
    /// </summary>
    private static FfprobeFailureCategory ClassifyFailure(string standardError)
    {
        if (standardError.Contains("403 Forbidden", StringComparison.OrdinalIgnoreCase)) { return FfprobeFailureCategory.HttpForbidden; }
        if (standardError.Contains("404 Not Found", StringComparison.OrdinalIgnoreCase)) { return FfprobeFailureCategory.HttpNotFound; }
        if (standardError.Contains("Server Error", StringComparison.OrdinalIgnoreCase)) { return FfprobeFailureCategory.HttpServerError; }
        if (standardError.Contains("Connection refused", StringComparison.OrdinalIgnoreCase) ||
            standardError.Contains("Failed to resolve hostname", StringComparison.OrdinalIgnoreCase)) { return FfprobeFailureCategory.ConnectionFailed; }
        if (standardError.Contains("timed out", StringComparison.OrdinalIgnoreCase)) { return FfprobeFailureCategory.Timeout; }
        if (standardError.Contains("Invalid data found", StringComparison.OrdinalIgnoreCase)) { return FfprobeFailureCategory.InvalidData; }

        return FfprobeFailureCategory.Unknown;
    }

    private enum FfprobeFailureCategory
    {
        Unknown,
        HttpForbidden,
        HttpNotFound,
        HttpServerError,
        ConnectionFailed,
        Timeout,
        InvalidData,
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Probed candidate {CandidateName}: approved={Approved} result={Result} ({ElapsedMs}ms)")]
    private static partial void LogProbeCompleted(ILogger logger, string candidateName, bool approved, SourceAttemptResult result, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ffprobe timed out probing candidate {CandidateName} after {ElapsedMs}ms")]
    private static partial void LogProbeTimeout(ILogger logger, string candidateName, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ffprobe exited with code {ExitCode} for candidate {CandidateName} (failure category: {FailureCategory})")]
    private static partial void LogProbeFailed(ILogger logger, string candidateName, int exitCode, FfprobeFailureCategory failureCategory);

    [LoggerMessage(Level = LogLevel.Error, Message = "ffprobe executable '{ExecutablePath}' is not available: {Reason}")]
    private static partial void LogExecutableUnavailable(ILogger logger, string executablePath, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected candidate {CandidateName}: URL scheme is not http/https")]
    private static partial void LogRejectedScheme(ILogger logger, string candidateName);
}
