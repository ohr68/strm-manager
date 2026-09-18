using System.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace StrmManager.Modules.MediaProcessing.Infrastructure.Validation.Ffprobe;

/// <summary>
/// A lightweight local executable-availability probe (runs "ffprobe -version" with a
/// short timeout) - never touches a remote stream. Registered with failureStatus:
/// Degraded, not Unhealthy: the rest of the service (Catalog CRUD, metadata sync) works
/// fine without ffprobe - only episode processing is unavailable, which doesn't warrant
/// taking the whole service out of rotation.
/// </summary>
public sealed class FfprobeHealthCheck(IOptions<FfprobeOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = options.Value.ExecutablePath,
            ArgumentList = { "-version" },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            using Process? process = Process.Start(startInfo);

            if (process is null)
            {
                return HealthCheckResult.Degraded("ffprobe process could not be started.");
            }

            await process.WaitForExitAsync(linkedCts.Token);

            return process.ExitCode == 0
                ? HealthCheckResult.Healthy("ffprobe is available.")
                : HealthCheckResult.Degraded($"ffprobe exited with code {process.ExitCode}.");
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or OperationCanceledException)
        {
            return HealthCheckResult.Degraded($"ffprobe is not available: {exception.Message}");
        }
    }
}
