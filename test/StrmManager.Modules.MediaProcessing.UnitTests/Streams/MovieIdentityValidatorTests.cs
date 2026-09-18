using StrmManager.Modules.MediaProcessing.Application.Streams.MovieIdentity;

namespace StrmManager.Modules.MediaProcessing.UnitTests.Streams;

/// <summary>
/// Option A (positive evidence only): a candidate proceeds only on explicit structured evidence
/// - a matching IMDb id, a matching TMDB id, or an explicit matching "(YYYY)" - and never on a
/// title, however well it "matches". Any explicit contradiction wins over any positive evidence.
/// Written generically (no title in the API at all): the Lion King collision class - one title,
/// two movies, candidate text with no year/id - is modelled by <see cref="SameTitleDifferentYears"/>.
/// </summary>
public class MovieIdentityValidatorTests
{
    // Expected movie: IMDb tt0110357, TMDB 8587, 1994.
    private static readonly MovieIdentityReference Expected = new("tt0110357", "8587", 1994);

    private static MovieIdentityMatch Evaluate(string? description, string name = "FrostStream 1080p", string? candidateTmdbId = null, MovieIdentityReference? expected = null) =>
        MovieIdentityValidator.Evaluate(name, description, candidateTmdbId, expected ?? Expected);

    // --- IMDb id ---

    [Fact]
    public void MatchingImdbId_IsConfirmed() =>
        Assert.Equal(MovieIdentityMatch.Confirmed, Evaluate("🎬 Some Title tt0110357"));

    [Fact]
    public void MatchingImdbId_IsCaseInsensitive() =>
        Assert.Equal(MovieIdentityMatch.Confirmed, Evaluate("TT0110357"));

    [Fact]
    public void MatchingImdbId_InTheName_IsConfirmed() =>
        Assert.Equal(MovieIdentityMatch.Confirmed, Evaluate(description: null, name: "tt0110357 1080p"));

    [Fact]
    public void DifferentImdbId_IsConflicting() =>
        Assert.Equal(MovieIdentityMatch.Conflicting, Evaluate("🎬 Some Title tt6105098"));

    [Fact]
    public void BothMatchingAndDifferentImdbIds_IsConflicting() =>
        Assert.Equal(MovieIdentityMatch.Conflicting, Evaluate("tt0110357 tt6105098"));

    [Theory]
    [InlineData("xtt0110357")]
    [InlineData("tt0110357x")]
    [InlineData("att01103570")]
    public void ImdbIdInsideAnotherWord_IsNotAnIdAtAll(string text) =>
        Assert.Equal(MovieIdentityMatch.Undetermined, Evaluate(text));

    // --- TMDB id (structured field only) ---

    [Fact]
    public void MatchingTmdbId_IsConfirmed() =>
        Assert.Equal(MovieIdentityMatch.Confirmed, Evaluate("🎬 Titulo", candidateTmdbId: "8587"));

    [Fact]
    public void DifferentTmdbId_IsConflicting() =>
        Assert.Equal(MovieIdentityMatch.Conflicting, Evaluate("🎬 Titulo", candidateTmdbId: "420818"));

    [Fact]
    public void CandidateTmdbId_WhenTheExpectedMovieHasNone_IsNeitherProofNorConflict()
    {
        var withoutTmdb = new MovieIdentityReference("tt0110357", TmdbId: null, 1994);

        Assert.Equal(MovieIdentityMatch.Undetermined, Evaluate("🎬 Titulo", candidateTmdbId: "420818", expected: withoutTmdb));
    }

    [Fact]
    public void CandidateTmdbId_WhenTheExpectedMovieHasNone_DoesNotBlockOtherEvidence()
    {
        var withoutTmdb = new MovieIdentityReference("tt0110357", TmdbId: null, 1994);

        Assert.Equal(MovieIdentityMatch.Compatible, Evaluate("🎬 Titulo (1994)", candidateTmdbId: "420818", expected: withoutTmdb));
    }

    [Fact]
    public void TmdbIdInDisplayText_IsNotStructuredEvidence() =>
        Assert.Equal(MovieIdentityMatch.Undetermined, Evaluate("🎬 Titulo 8587"));

    // --- Explicit year ---

    [Fact]
    public void MatchingExplicitYear_IsCompatible() =>
        Assert.Equal(MovieIdentityMatch.Compatible, Evaluate("🎬 O Rei Leão (1994)\n🌊 Space\n🌎 Português"));

    [Fact]
    public void ConflictingExplicitYear_IsConflicting() =>
        Assert.Equal(MovieIdentityMatch.Conflicting, Evaluate("🎬 O Rei Leão (2019)\n🌊 Space\n🌎 Português"));

    [Fact]
    public void MatchingYearInTheName_IsCompatible() =>
        Assert.Equal(MovieIdentityMatch.Compatible, Evaluate(description: null, name: "Some Title (1994) 1080p"));

    [Fact]
    public void TwoYears_OneMatchingOneNot_IsConflicting() =>
        Assert.Equal(MovieIdentityMatch.Conflicting, Evaluate("🎬 Title (1994) (2019)"));

    [Fact]
    public void MatchingYearOnlyOnALaterLine_IsNotPositiveEvidence() =>
        // Positive evidence comes from the title line (and the name) only - an incidental
        // "(1994)" on a source/language line proves nothing.
        Assert.Equal(MovieIdentityMatch.Undetermined, Evaluate("🎬 Titulo\n🌊 Space (1994)\n🌎 Português"));

    [Fact]
    public void ConflictingYearOnALaterLine_IsStillAContradiction() =>
        Assert.Equal(MovieIdentityMatch.Conflicting, Evaluate("🎬 Titulo\n🌊 Space (2019)\n🌎 Português"));

    // --- Nothing to go on ---

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("🎬 O Rei Leão\n🌊 Space\n🌎 Português")]
    public void NoStructuredEvidence_IsUndetermined(string? description) =>
        Assert.Equal(MovieIdentityMatch.Undetermined, Evaluate(description));

    [Theory]
    [InlineData("The Lion King")]
    [InlineData("the lion king")]
    [InlineData("THE LION KING (THE)")]
    [InlineData("O Rei Leão")]
    [InlineData("Rei Leao")]
    public void ATitleAlone_IsNeverProof_HoweverItMatches(string title) =>
        Assert.Equal(MovieIdentityMatch.Undetermined, Evaluate($"🎬 {title}"));

    [Fact]
    public void PlainNameAndQualityTag_IsUndetermined() =>
        Assert.Equal(MovieIdentityMatch.Undetermined, Evaluate(description: null, name: "FrostStream 1080p"));

    // --- Punctuation / Unicode must never become proof ---

    [Theory]
    [InlineData("🎬 Titulo （1994）")] // full-width parentheses
    [InlineData("🎬 Titulo (١٩٩٤)")] // Arabic-Indic digits
    [InlineData("🎬 Titulo (１９９４)")] // full-width digits
    [InlineData("🎬 Titulo [1994]")]
    [InlineData("🎬 Titulo 1994")] // a bare number is not an explicit year
    [InlineData("🎬 Titulo (1994")] // unbalanced
    [InlineData("🎬 Titulo (94)")]
    [InlineData("🎬 Titulo (1994p)")]
    [InlineData("🎬 1994")] // a movie literally titled "1994"
    public void NearMissesOfAYear_AreNotYearEvidence(string description) =>
        Assert.Equal(MovieIdentityMatch.Undetermined, Evaluate(description));

    [Theory]
    [InlineData("🎬 Titulo (2160)")] // resolution-like number outside the plausible year range
    [InlineData("🎬 Titulo (1080)")]
    [InlineData("🎬 Titulo (0000)")]
    public void ParenthesisedNumbersThatCannotBeAYear_AreIgnoredNotConflicts(string description) =>
        Assert.Equal(MovieIdentityMatch.Undetermined, Evaluate(description));

    [Theory]
    [InlineData("Missão: Impossível")]
    [InlineData("Mission: Impossible")]
    [InlineData("WALL·E")]
    [InlineData("Amélie")]
    [InlineData("(500) Days of Summer")]
    [InlineData("Se7en")]
    public void PunctuationAndDiacriticsInTitles_NeverCreateEvidence(string title) =>
        Assert.Equal(MovieIdentityMatch.Undetermined, Evaluate($"🎬 {title}"));

    // --- Precedence: contradiction always wins ---

    [Fact]
    public void MatchingTmdbButConflictingYear_IsConflictingNotConfirmed() =>
        // The example from the policy: expected IMDb A / TMDB X / 1994; candidate TMDB X, year 2019.
        Assert.Equal(MovieIdentityMatch.Conflicting, Evaluate("🎬 Titulo (2019)", candidateTmdbId: "8587"));

    [Fact]
    public void MatchingImdbButConflictingYear_IsConflicting() =>
        Assert.Equal(MovieIdentityMatch.Conflicting, Evaluate("🎬 Titulo (2019) tt0110357"));

    [Fact]
    public void MatchingImdbButConflictingTmdb_IsConflicting() =>
        Assert.Equal(MovieIdentityMatch.Conflicting, Evaluate("tt0110357", candidateTmdbId: "420818"));

    [Fact]
    public void MatchingYearButConflictingTmdb_IsConflicting() =>
        Assert.Equal(MovieIdentityMatch.Conflicting, Evaluate("🎬 Titulo (1994)", candidateTmdbId: "420818"));

    [Fact]
    public void MatchingYearButConflictingImdb_IsConflicting() =>
        Assert.Equal(MovieIdentityMatch.Conflicting, Evaluate("🎬 Titulo (1994) tt6105098"));

    [Fact]
    public void ImdbAndTmdbAndYearAllMatching_IsConfirmed() =>
        Assert.Equal(MovieIdentityMatch.Confirmed, Evaluate("🎬 Titulo (1994) tt0110357", candidateTmdbId: "8587"));

    [Fact]
    public void StrongEvidenceOutranksYearEvidence_WhenNothingContradicts() =>
        Assert.Equal(MovieIdentityMatch.Confirmed, Evaluate("🎬 Titulo (1994)", candidateTmdbId: "8587"));

    // --- The Lion King collision class, generically ---

    /// <summary>
    /// One title shared by two different movies (different years, IMDb ids and TMDB ids). FrostStream
    /// returned the same title-only candidates for both; the validator must not let either
    /// through, and must accept the candidate for a movie only when the text explicitly says so.
    /// </summary>
    public static TheoryData<MovieIdentityReference> SameTitleDifferentYears() => new()
    {
        new MovieIdentityReference("tt0110357", "8587", 1994),
        new MovieIdentityReference("tt6105098", "420818", 2019),
    };

    [Theory]
    [MemberData(nameof(SameTitleDifferentYears))]
    public void SameTitleCandidateWithNoYearOrId_IsUndeterminedForEitherMovie(MovieIdentityReference expected) =>
        Assert.Equal(MovieIdentityMatch.Undetermined, Evaluate("🎬 O REI LEÃO\n🌊 Space\n🌎 Português", "FrostStream 4K", expected: expected));

    [Fact]
    public void SameTitleCandidate_WithAnExplicitYear_IsCompatibleOnlyForTheMovieItNames()
    {
        const string description = "🎬 O REI LEÃO (2019)\n🌊 Space\n🌎 Português";

        Assert.Equal(MovieIdentityMatch.Compatible, Evaluate(description, expected: new MovieIdentityReference("tt6105098", "420818", 2019)));
        Assert.Equal(MovieIdentityMatch.Conflicting, Evaluate(description, expected: new MovieIdentityReference("tt0110357", "8587", 1994)));
    }
}
