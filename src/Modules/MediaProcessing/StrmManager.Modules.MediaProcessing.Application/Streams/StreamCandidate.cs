using System.Text;

namespace StrmManager.Modules.MediaProcessing.Application.Streams;

/// <summary>
/// Provider-neutral stream candidate. Url is transient/sensitive data - it exists only
/// long enough to validate and write the .strm file; never logged, never persisted (see
/// SourceAttempt/StrmFile, which deliberately do not store it).
///
/// TmdbId is optional STRUCTURED identity metadata: set only when the provider exposed a TMDB
/// id in a form it is known to use (never parsed out of the URL, never guessed from a title).
/// RequiresCustomHeaders says the provider marked the stream as needing custom HTTP request
/// headers (User-Agent/Referer/Origin...) - only that fact is kept, never the header values,
/// because a plain .strm file cannot carry them and the current validator cannot send them.
/// </summary>
public sealed record StreamCandidate(
    string Provider,
    string Name,
    string? Description,
    string Url,
    string? TmdbId = null,
    bool RequiresCustomHeaders = false)
{
    // The compiler-generated ToString would print every property, Url included - which would
    // put a signed stream URL into any accidental log line or failing-assertion message.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Provider = ").Append(Provider)
            .Append(", Name = ").Append(Name)
            .Append(", TmdbId = ").Append(TmdbId)
            .Append(", RequiresCustomHeaders = ").Append(RequiresCustomHeaders);

        return true;
    }
}
