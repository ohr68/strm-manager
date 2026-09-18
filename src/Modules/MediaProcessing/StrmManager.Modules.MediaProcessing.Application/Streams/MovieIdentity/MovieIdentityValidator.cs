using System.Globalization;
using System.Text.RegularExpressions;

namespace StrmManager.Modules.MediaProcessing.Application.Streams.MovieIdentity;

/// <summary>
/// Decides, from a candidate's own explicit evidence only, whether it is safe to spend a media
/// validation on it. Positive evidence only ("Option A"): a candidate proceeds solely on a
/// matching IMDb id, a matching structured TMDB id, or a matching explicit "(YYYY)". A title is
/// never evidence - not equal, not similar, not normalised - because FrostStream's titles are
/// localized and, worse, one title-keyed candidate list can be returned for several different
/// movies (the Lion King collision: 1994 and 2019 got the same candidates, none carrying a
/// year or an id). No evidence is Undetermined, and Undetermined is rejected.
///
/// Rules, evaluated over ALL the evidence before deciding:
///  1. Any contradiction -> Conflicting, whatever else matches: a different IMDb id, a different
///     TMDB id (only when the expected TMDB id is known), or a "(YYYY)" different from the
///     expected year.
///  2. Otherwise a matching IMDb id or TMDB id -> Confirmed.
///  3. Otherwise a matching "(YYYY)" -> Compatible.
///  4. Otherwise -> Undetermined.
///
/// Where the evidence is read from: IMDb ids from the name and the whole description (case-
/// insensitive, whole-token, ASCII); the TMDB id only from the structured candidate field, never
/// from display text or the URL; years only in the exact form "(YYYY)" with ASCII digits in a
/// plausible range. A year CONTRADICTS from anywhere in the name or description, but only
/// COUNTS as positive evidence on the name or the description's first (title) line - an
/// incidental year on a source/language line proves nothing.
///
/// Known limitation (accepted for the MVP): two different movies can share a title AND a release
/// year, so a matching year is sufficient, not conclusive. It is used because it is the only
/// broadly observed identity signal FrostStream currently provides.
/// </summary>
public static partial class MovieIdentityValidator
{
    public static MovieIdentityMatch Evaluate(
        string? name,
        string? description,
        string? candidateTmdbId,
        MovieIdentityReference expected)
    {
        string nameText = name ?? string.Empty;
        string descriptionText = description ?? string.Empty;

        bool contradicted = false;
        bool strongMatch = false;
        bool yearMatch = false;

        foreach (Match imdb in ImdbIdPattern().Matches($"{nameText}\n{descriptionText}"))
        {
            if (string.Equals(imdb.Value, expected.ImdbId, StringComparison.OrdinalIgnoreCase))
            {
                strongMatch = true;
            }
            else
            {
                contradicted = true;
            }
        }

        if (!string.IsNullOrWhiteSpace(expected.TmdbId) && !string.IsNullOrWhiteSpace(candidateTmdbId))
        {
            if (string.Equals(candidateTmdbId.Trim(), expected.TmdbId.Trim(), StringComparison.Ordinal))
            {
                strongMatch = true;
            }
            else
            {
                contradicted = true;
            }
        }

        foreach (int year in ExplicitYears($"{nameText}\n{descriptionText}"))
        {
            if (year != expected.Year)
            {
                contradicted = true;
            }
        }

        foreach (int year in ExplicitYears($"{nameText}\n{FirstLine(descriptionText)}"))
        {
            if (year == expected.Year)
            {
                yearMatch = true;
            }
        }

        if (contradicted)
        {
            return MovieIdentityMatch.Conflicting;
        }

        if (strongMatch)
        {
            return MovieIdentityMatch.Confirmed;
        }

        return yearMatch ? MovieIdentityMatch.Compatible : MovieIdentityMatch.Undetermined;
    }

    private static IEnumerable<int> ExplicitYears(string text) =>
        YearPattern().Matches(text).Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));

    private static string FirstLine(string text)
    {
        int end = text.IndexOf('\n', StringComparison.Ordinal);
        return end < 0 ? text : text[..end];
    }

    // Whole token, ASCII only, so "xtt0110357" or "tt0110357x" is not an IMDb id.
    [GeneratedRegex(@"(?<![A-Za-z0-9])tt[0-9]{7,10}(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ImdbIdPattern();

    // "(YYYY)" exactly: literal ASCII parentheses and digits (not full-width or Arabic-Indic
    // forms), 1800-2099 so a resolution-like "(1080)" or "(2160)" is never read as a year.
    [GeneratedRegex(@"\(((?:18|19|20)[0-9]{2})\)", RegexOptions.CultureInvariant)]
    private static partial Regex YearPattern();
}
