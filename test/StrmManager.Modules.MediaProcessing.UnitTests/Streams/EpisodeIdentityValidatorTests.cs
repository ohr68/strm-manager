using StrmManager.Modules.MediaProcessing.Application.Streams.EpisodeIdentity;

namespace StrmManager.Modules.MediaProcessing.UnitTests.Streams;

public class EpisodeIdentityValidatorTests
{
    [Theory]
    [InlineData("T01·E05")]
    [InlineData("S01E05")]
    [InlineData("T1 E5")]
    [InlineData("S1-E5")]
    [InlineData("t01.e05")]
    [InlineData("[Group] Show Name S01E05 1080p")]
    public void Evaluate_TextMatchingExpectedEpisode_ReturnsCompatible(string candidateText)
    {
        EpisodeIdentityMatch result = EpisodeIdentityValidator.Evaluate(candidateText, expectedSeasonNumber: 1, expectedEpisodeNumber: 5);

        Assert.Equal(EpisodeIdentityMatch.Compatible, result);
    }

    [Fact]
    public void Evaluate_TextReferencingDifferentEpisode_ReturnsConflicting()
    {
        EpisodeIdentityMatch result = EpisodeIdentityValidator.Evaluate("S01E06", expectedSeasonNumber: 1, expectedEpisodeNumber: 5);

        Assert.Equal(EpisodeIdentityMatch.Conflicting, result);
    }

    [Fact]
    public void Evaluate_TextReferencingDifferentSeason_ReturnsConflicting()
    {
        EpisodeIdentityMatch result = EpisodeIdentityValidator.Evaluate("S02E05", expectedSeasonNumber: 1, expectedEpisodeNumber: 5);

        Assert.Equal(EpisodeIdentityMatch.Conflicting, result);
    }

    [Fact]
    public void Evaluate_TextWithMixedConflictingReferences_ReturnsConflicting()
    {
        // One reference matches expected, another does not - legacy behavior rejects
        // this rather than trusting either.
        EpisodeIdentityMatch result = EpisodeIdentityValidator.Evaluate("S01E05 also known as T01·E06", expectedSeasonNumber: 1, expectedEpisodeNumber: 5);

        Assert.Equal(EpisodeIdentityMatch.Conflicting, result);
    }

    [Fact]
    public void Evaluate_TextWithNoDetectableReference_ReturnsUndetermined()
    {
        EpisodeIdentityMatch result = EpisodeIdentityValidator.Evaluate("Random Movie Title 1080p", expectedSeasonNumber: 1, expectedEpisodeNumber: 5);

        Assert.Equal(EpisodeIdentityMatch.Undetermined, result);
    }

    [Fact]
    public void Evaluate_EmptyText_ReturnsUndetermined()
    {
        EpisodeIdentityMatch result = EpisodeIdentityValidator.Evaluate(string.Empty, expectedSeasonNumber: 1, expectedEpisodeNumber: 5);

        Assert.Equal(EpisodeIdentityMatch.Undetermined, result);
    }

    [Fact]
    public void Evaluate_RepeatedIdenticalMatchingReference_ReturnsCompatible()
    {
        EpisodeIdentityMatch result = EpisodeIdentityValidator.Evaluate("S01E05 - S01E05 (dual audio)", expectedSeasonNumber: 1, expectedEpisodeNumber: 5);

        Assert.Equal(EpisodeIdentityMatch.Compatible, result);
    }
}
