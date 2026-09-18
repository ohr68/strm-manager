using StrmManager.Common.Domain.Abstractions;

namespace StrmManager.Modules.MediaProcessing.Application.StrmGeneration;

public static class StrmWriterErrors
{
    public static Error PathEscapesRoot(string reason) =>
        Error.Failure("Strm.PathEscapesRoot", $"Refusing to write outside the configured STRM root: {reason}");

    public static Error WriteFailed(string reason) =>
        Error.Failure("Strm.WriteFailed", $"Failed to write the .strm file: {reason}");
}
