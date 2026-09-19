using System.Text;

namespace StrmManager.Modules.MediaProcessing.UnitTests.Validation;

/// <summary>
/// A throwaway script standing in for ffprobe (a .cmd on Windows, an sh script elsewhere), so the
/// failure paths of FfprobeMediaValidator can be exercised without ffprobe being installed. The
/// script ignores its arguments and emits fixed text, so the test controls exactly what "ffprobe"
/// prints - including a media URL echoed on stderr, as the real ffprobe does when it fails.
/// </summary>
internal sealed class FakeFfprobeExecutable : IDisposable
{
    private readonly string _directory;

    private FakeFfprobeExecutable(string directory, string path)
    {
        _directory = directory;
        Path = path;
    }

    public string Path { get; }

    /// <summary>Prints <paramref name="stderr"/> on stderr and <paramref name="stdout"/> on stdout, then exits with <paramref name="exitCode"/>.</summary>
    public static FakeFfprobeExecutable Emitting(string stderr, string stdout, int exitCode) =>
        Create(Script(stderr, stdout, exitCode, sleepSeconds: 0));

    /// <summary>Prints <paramref name="stderr"/> on stderr, then stays alive far longer than any test timeout.</summary>
    public static FakeFfprobeExecutable Hanging(string stderr) =>
        Create(Script(stderr, string.Empty, 0, sleepSeconds: 60));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup of a temp directory; a lingering killed child may still hold it.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static FakeFfprobeExecutable Create(Func<bool, string> script)
    {
        string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "strm-fake-ffprobe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        bool windows = OperatingSystem.IsWindows();
        string path = System.IO.Path.Combine(directory, windows ? "fake-ffprobe.cmd" : "fake-ffprobe.sh");
        File.WriteAllText(path, script(windows), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        if (!windows)
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return new FakeFfprobeExecutable(directory, path);
    }

    private static Func<bool, string> Script(string stderr, string stdout, int exitCode, int sleepSeconds) => windows =>
    {
        var text = new StringBuilder();

        if (windows)
        {
            // Test texts must avoid cmd metacharacters (& | < > ^ %); the canary URLs do.
            text.Append("@echo off\r\n");
            if (stderr.Length > 0) { text.Append("echo ").Append(stderr).Append(" 1>&2\r\n"); }
            if (stdout.Length > 0) { text.Append("echo ").Append(stdout).Append("\r\n"); }
            if (sleepSeconds > 0) { text.Append("ping -n ").Append(sleepSeconds + 1).Append(" 127.0.0.1 >nul\r\n"); }
            text.Append("exit /b ").Append(exitCode).Append("\r\n");
        }
        else
        {
            text.Append("#!/bin/sh\n");
            if (stderr.Length > 0) { text.Append("printf '%s\\n' '").Append(stderr).Append("' >&2\n"); }
            if (stdout.Length > 0) { text.Append("printf '%s\\n' '").Append(stdout).Append("'\n"); }
            if (sleepSeconds > 0) { text.Append("sleep ").Append(sleepSeconds).Append('\n'); }
            text.Append("exit ").Append(exitCode).Append('\n');
        }

        return text.ToString();
    };
}
