using System.Text;
using StrmManager.Modules.MediaProcessing.Application.Streams;
using StrmManager.Modules.MediaProcessing.Application.Validation;

namespace StrmManager.Modules.MediaProcessing.Application.Selection;

/// <summary>Where an episode candidate's evaluation ended.</summary>
public enum EpisodeCandidateStage
{
    /// <summary>Rejected before ffprobe because the candidate does not identify the requested episode.</summary>
    IdentityConflicting,

    /// <summary>Rejected before ffprobe because the candidate's episode identity could not be confirmed.</summary>
    IdentityUndetermined,

    /// <summary>Episode identity passed, so the media validator ran.</summary>
    MediaValidation,
}

/// <summary>
/// One episode candidate's evaluation.
/// </summary>
public sealed record EpisodeCandidateEvaluation(
    StreamCandidate Candidate,
    EpisodeCandidateStage Stage,
    MediaValidationResult Result)
{
    public bool Approved => Result.Approved;

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
