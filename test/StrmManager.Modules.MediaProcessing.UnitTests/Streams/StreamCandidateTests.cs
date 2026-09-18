using StrmManager.Modules.MediaProcessing.Application.Streams;

namespace StrmManager.Modules.MediaProcessing.UnitTests.Streams;

public class StreamCandidateTests
{
    [Fact]
    public void ToString_NeverContainsTheUrl()
    {
        var candidate = new StreamCandidate("FrostStream", "FrostStream 1080p", "🎬 Titulo (1994)", "https://media.example.test/video?token=URL-SECRET");

        string text = candidate.ToString();

        Assert.DoesNotContain("media.example.test", text, StringComparison.Ordinal);
        Assert.DoesNotContain("URL-SECRET", text, StringComparison.Ordinal);
        Assert.Contains("FrostStream 1080p", text, StringComparison.Ordinal); // still useful for diagnostics
    }

    [Fact]
    public void OptionalStructuredFields_DefaultToNoTmdbIdAndNoRequiredHeaders()
    {
        var candidate = new StreamCandidate("FrostStream", "Name", null, "https://media.example.test/video");

        Assert.Null(candidate.TmdbId);
        Assert.False(candidate.RequiresCustomHeaders);
    }

    [Fact]
    public void ValueEquality_StillWorksAndIncludesTheNewFields()
    {
        var plain = new StreamCandidate("P", "N", null, "https://a.example.test");

        Assert.Equal(plain, new StreamCandidate("P", "N", null, "https://a.example.test"));
        Assert.NotEqual(plain, plain with { TmdbId = "1" });
        Assert.NotEqual(plain, plain with { RequiresCustomHeaders = true });
    }
}
