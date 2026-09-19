using System.Text;
using StrmManager.Modules.MediaProcessing.Application.Streams;
using StrmManager.Modules.MediaProcessing.Application.Validation;

namespace StrmManager.Modules.MediaProcessing.Application.Selection;

/// <summary>Where a candidate's evaluation ended.</summary>
public enum MovieCandidateStage
{
    /// <summary>Rejected before identity or ffprobe: the stream needs custom request headers a plain .strm cannot carry.</summary>
    UnsupportedHeaders,

    /// <summary>Rejected before ffprobe: the candidate's own evidence names a different movie.</summary>
    IdentityConflicting,

    /// <summary>Rejected before ffprobe: no positive evidence that the candidate is this movie.</summary>
    IdentityUndetermined,

    /// <summary>Identity passed, so the media validator ran; <see cref="MovieCandidateEvaluation.Result"/> is its verdict.</summary>
    MediaValidation,
}

/// <summary>
/// One candidate's outcome. <see cref="Result"/> carries exactly what a SourceAttempt records (result, durations,
/// codecs, fixed failure reason); for a candidate turned away before ffprobe it is a Rejected result with the
/// movie's expected runtime and a fixed, URL-free reason.
/// </summary>
public sealed record MovieCandidateEvaluation(StreamCandidate Candidate, MovieCandidateStage Stage, MediaValidationResult Result)
{
    public bool Approved => Result.Approved;

    // The generated ToString would print every member. Candidate.Url must never reach a log or a failing-assertion
    // message, so print only what identifies the candidate and the verdict - independent of how the other types print.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Provider = ").Append(Candidate.Provider)
            .Append(", Name = ").Append(Candidate.Name)
            .Append(", Stage = ").Append(Stage)
            .Append(", Result = ").Append(Result.Result)
            .Append(", Approved = ").Append(Approved);

        return true;
    }
}
