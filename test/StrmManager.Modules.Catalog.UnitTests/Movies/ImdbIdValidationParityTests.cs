using StrmManager.Modules.Catalog.Application.Movies.AddMovie;
using StrmManager.Modules.Catalog.Application.Movies.GetMovieByImdbId;

namespace StrmManager.Modules.Catalog.UnitTests.Movies;

/// <summary>
/// Adding a movie and looking one up by IMDb id share one IMDb-id rule (ImdbIdRules). These pin that the two validators still accept and
/// reject exactly the same forms, and pin the rule itself ("tt" lowercase + 1..15 digits, at most 32 characters), so neither can drift.
/// </summary>
public class ImdbIdValidationParityTests
{
    private static readonly AddMovieCommandValidator Add = new();
    private static readonly GetMovieByImdbIdQueryValidator Lookup = new();

    [Theory]
    [InlineData("tt0111161", true)]
    [InlineData("tt1", true)]
    [InlineData("tt123456789012345", true)]     // 15 digits: the longest accepted
    [InlineData("tt1234567890123456", false)]   // 16 digits
    [InlineData("tt", false)]
    [InlineData("TT0111161", false)]            // lowercase prefix only
    [InlineData("Tt0111161", false)]
    [InlineData("0111161", false)]
    [InlineData("ttabc", false)]
    [InlineData("tt0111161 ", false)]
    [InlineData(" tt0111161", false)]
    [InlineData("tt0111161tt0111161tt0111161tt0111161", false)]
    [InlineData("tt1/../series/tt2", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void BothValidators_ApplyTheSameRule(string? imdbId, bool valid)
    {
        Assert.Equal(valid, Add.Validate(new AddMovieCommand(imdbId!)).IsValid);
        Assert.Equal(valid, Lookup.Validate(new GetMovieByImdbIdQuery(imdbId!)).IsValid);
    }

    // The pre-existing AddMovie rule ("^tt\d{1,15}$") is preserved as-is, and it is looser than "ASCII only, no trailing whitespace": '$' also
    // matches before a final newline and \d matches non-ASCII decimal digits. Tightening that is a separate change to AddMovie's contract; here we
    // only pin that lookup stays in exact step with it. (Such an id can only ever miss - the value is a bound query parameter.)
    [Theory]
    [InlineData("tt0111161\n")]
    [InlineData("tt01111١")]
    public void BothValidators_ShareTheExistingRule_EvenWhereItIsLoose(string imdbId)
    {
        Assert.Equal(Add.Validate(new AddMovieCommand(imdbId)).IsValid, Lookup.Validate(new GetMovieByImdbIdQuery(imdbId)).IsValid);
    }

    [Theory]
    [InlineData("tt")]
    [InlineData("TT0111161")]
    [InlineData("")]
    public void BothValidators_ReportTheFailureOnTheImdbIdProperty(string imdbId)
    {
        Assert.Equal(
            Add.Validate(new AddMovieCommand(imdbId)).Errors.Select(e => e.PropertyName).Distinct(),
            Lookup.Validate(new GetMovieByImdbIdQuery(imdbId)).Errors.Select(e => e.PropertyName).Distinct());
    }
}
