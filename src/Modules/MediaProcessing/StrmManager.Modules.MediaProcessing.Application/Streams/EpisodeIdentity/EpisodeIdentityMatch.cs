namespace StrmManager.Modules.MediaProcessing.Application.Streams.EpisodeIdentity;

public enum EpisodeIdentityMatch
{
    /// <summary>Text contains at least one episode reference, all of which agree with the expected episode.</summary>
    Compatible,

    /// <summary>Text contains at least one episode reference that does not match the expected episode.</summary>
    Conflicting,

    /// <summary>Text contains no detectable episode reference at all.</summary>
    Undetermined,
}
