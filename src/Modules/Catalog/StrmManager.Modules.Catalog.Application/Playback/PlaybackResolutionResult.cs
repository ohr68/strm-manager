using System.Diagnostics;

namespace StrmManager.Modules.Catalog.Application.Playback;

/// <summary>
/// Why a Completed movie could not be resolved to a playable source right now. Stable, closed, and free of
/// provider/validator text on purpose - it is safe to log, count and (later) map to a generic response.
/// </summary>
public enum PlaybackUnavailableReason
{
    /// <summary>The movie has no IMDb id to look streams up by.</summary>
    MissingImdbId,

    /// <summary>The stream provider reported a technical failure (unavailable, timeout, invalid response).</summary>
    ProviderFailure,

    /// <summary>The provider answered, but with no candidates.</summary>
    NoCandidates,

    /// <summary>Candidates were returned, but none passed the header, identity and media checks.</summary>
    NoApprovedCandidate,
}

/// <summary>
/// The outcome of resolving a movie for playback: a closed set of cases, so an expected outcome is a value,
/// never an exception. Only <see cref="Resolved"/> carries a provider URL, and only inside a
/// <see cref="PlaybackLocation"/> that cannot be printed by accident.
/// </summary>
public abstract record PlaybackResolutionResult
{
    // Closed: the private constructor lets only the nested cases below derive from this type.
    private PlaybackResolutionResult()
    {
    }

    /// <summary>The movie does not exist or is not Completed. Deliberately does not say which.</summary>
    public sealed record NotFound : PlaybackResolutionResult;

    /// <summary>The movie is Completed, but no playable source could be produced right now.</summary>
    public sealed record Unavailable(PlaybackUnavailableReason Reason) : PlaybackResolutionResult;

    /// <summary>
    /// A candidate passed every check. <see cref="Provider"/> and <see cref="SourceName"/> identify it for
    /// diagnostics (the same two fields ProcessMovie already reports); the URL is in <see cref="Location"/>.
    /// </summary>
    public sealed record Resolved(string Provider, string SourceName, PlaybackLocation Location) : PlaybackResolutionResult;
}

/// <summary>
/// A complete provider media URL, wrapped so it can travel inside a result without leaking. It is the ONLY value in
/// the playback path allowed to hold the URL and it exists for one purpose: the Location header of the redirect.
/// ToString, formatting, structured logging and JSON serialization all yield no URL; the debugger hides it;
/// the sole way to read it is the explicit <see cref="Reveal"/>.
/// </summary>
[DebuggerDisplay("[redacted]")]
public sealed record PlaybackLocation
{
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly string _url;

    public PlaybackLocation(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        _url = url;
    }

    /// <summary>The URL itself. Call this only where the URL is meant to leave the service.</summary>
    public string Reveal() => _url;

    public override string ToString() => "[redacted]";
}
