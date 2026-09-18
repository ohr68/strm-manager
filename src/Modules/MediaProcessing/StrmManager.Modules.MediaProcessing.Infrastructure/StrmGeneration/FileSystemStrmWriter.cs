using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.MediaProcessing.Application.StrmGeneration;

namespace StrmManager.Modules.MediaProcessing.Infrastructure.StrmGeneration;

/// <summary>
/// Writes Jellyfin-compatible .strm files under the configured root:
/// {root}/tv/{Series Title} ({Year})/Season {NN}/{Series Title} - S{NN}E{NN}.strm
/// See ADR-010 for the path-sanitization/traversal-protection and atomic-write reasoning.
/// </summary>
internal sealed partial class FileSystemStrmWriter(IOptions<StrmOptions> options, ILogger<FileSystemStrmWriter> logger)
    : IStrmWriter
{
    public async Task<Result<string>> WriteEpisodeAsync(
        EpisodeStrmReference reference,
        string sourceUrl,
        CancellationToken cancellationToken = default)
    {
        string root = Path.GetFullPath(options.Value.RootPath);

        string sanitizedTitle = SanitizePathComponent(reference.SeriesTitle);
        string seriesFolder = $"{sanitizedTitle} ({reference.SeriesYear})";
        string seasonFolder = $"Season {reference.SeasonNumber:D2}";
        string fileName = $"{sanitizedTitle} - S{reference.SeasonNumber:D2}E{reference.EpisodeNumber:D2}.strm";

        string candidatePath = Path.GetFullPath(Path.Combine(root, "tv", seriesFolder, seasonFolder, fileName));

        // After building the final path, verify it remains under the configured root -
        // the last line of defense even though SanitizePathComponent already strips
        // path separators and ".." sequences from each component individually.
        if (!IsUnderRoot(candidatePath, root))
        {
            LogPathEscapedRoot(logger, reference.SeriesTitle);
            return Result.Failure<string>(StrmWriterErrors.PathEscapesRoot("computed path resolved outside the STRM root"));
        }

        string directory = Path.GetDirectoryName(candidatePath)!;

        try
        {
            Directory.CreateDirectory(directory);

            string tempPath = Path.Combine(directory, $".{Guid.NewGuid():N}.strm.tmp");

            await File.WriteAllTextAsync(tempPath, sourceUrl, cancellationToken);

            File.Move(tempPath, candidatePath, overwrite: true);

            LogStrmWritten(logger, reference.SeasonNumber, reference.EpisodeNumber);

            return candidatePath;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogWriteFailed(logger, reference.SeasonNumber, reference.EpisodeNumber, exception.Message);
            return Result.Failure<string>(StrmWriterErrors.WriteFailed(exception.Message));
        }
    }

    private static bool IsUnderRoot(string candidatePath, string root)
    {
        string normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidatePath.StartsWith(normalizedRoot, StringComparison.Ordinal);
    }

    // A fixed, OS-independent denylist (the Windows-reserved set, a superset of what
    // Linux itself rejects) rather than Path.GetInvalidFileNameChars() - that method is
    // platform-dependent, and this service always runs in a Linux container while its
    // .strm output is typically also browsed from Windows/SMB clients via Jellyfin.
    // Using the platform's own (smaller, Linux) set would produce filenames that are
    // valid where they're written but broken wherever else they're read.
    private static readonly char[] InvalidFileNameChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    private static string SanitizePathComponent(string value)
    {
        // Strip directory separators and ".." outright - external/provider-derived text
        // (here, catalog metadata that ultimately traces back to a provider) must never
        // be treated as a trusted filesystem path fragment.
        string collapsed = value.Replace("..", string.Empty, StringComparison.Ordinal);

        char[] buffer = new char[collapsed.Length];

        for (int i = 0; i < collapsed.Length; i++)
        {
            char character = collapsed[i];
            bool isInvalid = Array.IndexOf(InvalidFileNameChars, character) >= 0 || char.IsControl(character);
            buffer[i] = isInvalid ? '_' : character;
        }

        string sanitized = new string(buffer).Trim();

        return string.IsNullOrWhiteSpace(sanitized) ? "Untitled" : sanitized;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Wrote .strm file for S{SeasonNumber:D2}E{EpisodeNumber:D2}")]
    private static partial void LogStrmWritten(ILogger logger, int seasonNumber, int episodeNumber);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to write .strm file for S{SeasonNumber:D2}E{EpisodeNumber:D2}: {Reason}")]
    private static partial void LogWriteFailed(ILogger logger, int seasonNumber, int episodeNumber, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Computed .strm path for series '{SeriesTitle}' escaped the configured STRM root - refused to write")]
    private static partial void LogPathEscapedRoot(ILogger logger, string seriesTitle);
}
