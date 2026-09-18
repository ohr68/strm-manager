namespace StrmManager.Modules.MediaProcessing.Application.Streams.MovieIdentity;

public enum MovieIdentityMatch
{
    /// <summary>No explicit IMDb id, TMDB id or matching year - nothing positive to go on. Rejected.</summary>
    Undetermined = 0,

    /// <summary>An explicit "(YYYY)" equal to the expected year, and no contradiction. Sufficient for the MVP, not proof.</summary>
    Compatible = 1,

    /// <summary>A matching explicit IMDb id or structured TMDB id, and no contradiction.</summary>
    Confirmed = 2,

    /// <summary>Some explicit identity evidence contradicts the expected movie. Always rejected.</summary>
    Conflicting = 3,
}
