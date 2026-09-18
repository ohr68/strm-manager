using System.Text.RegularExpressions;

namespace StrmManager.Modules.MediaProcessing.Application.Streams.EpisodeIdentity;

/// <summary>
/// Ports the legacy PowerShell prototype's episode-identity safety check: a candidate's
/// name/title is scanned for "T01·E05"/"S01E05"/"T1 E5"/"S1-E5"-style references and
/// compared against the expected episode. Conservative by design - a candidate with no
/// detectable reference is treated the same as one with a conflicting reference
/// (rejected), matching the legacy behavior of never accepting a source whose episode
/// identity could not be positively confirmed.
/// </summary>
public static partial class EpisodeIdentityValidator
{
    public static EpisodeIdentityMatch Evaluate(string candidateText, int expectedSeasonNumber, int expectedEpisodeNumber)
    {
        MatchCollection matches = EpisodeReferencePattern().Matches(candidateText);

        if (matches.Count == 0)
        {
            return EpisodeIdentityMatch.Undetermined;
        }

        bool anyMatchesExpected = false;
        bool anyConflicts = false;

        foreach (Match match in matches)
        {
            int season = int.Parse(match.Groups[1].Value);
            int episode = int.Parse(match.Groups[2].Value);

            if (season == expectedSeasonNumber && episode == expectedEpisodeNumber)
            {
                anyMatchesExpected = true;
            }
            else
            {
                anyConflicts = true;
            }
        }

        if (anyConflicts)
        {
            return EpisodeIdentityMatch.Conflicting;
        }

        return anyMatchesExpected ? EpisodeIdentityMatch.Compatible : EpisodeIdentityMatch.Undetermined;
    }

    [GeneratedRegex(@"(?:T|S)\s*(\d{1,2})\s*[·.\- ]?\s*E\s*(\d{1,3})", RegexOptions.IgnoreCase)]
    private static partial Regex EpisodeReferencePattern();
}
