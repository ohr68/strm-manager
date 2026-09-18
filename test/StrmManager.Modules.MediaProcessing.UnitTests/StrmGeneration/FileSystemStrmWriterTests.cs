using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.MediaProcessing.Application.StrmGeneration;
using StrmManager.Modules.MediaProcessing.Infrastructure.StrmGeneration;

namespace StrmManager.Modules.MediaProcessing.UnitTests.StrmGeneration;

/// <summary>Every test uses its own temp directory - never the real /stream path.</summary>
public sealed class FileSystemStrmWriterTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("strm-manager-tests-");

    private FileSystemStrmWriter CreateWriter() =>
        new(Options.Create(new StrmOptions { RootPath = _root.FullName }), NullLogger<FileSystemStrmWriter>.Instance);

    [Fact]
    public async Task WriteEpisodeAsync_ProducesJellyfinCompatiblePath()
    {
        FileSystemStrmWriter writer = CreateWriter();
        var reference = new EpisodeStrmReference("Paradise", 2025, 1, 5);

        Result<string> result = await writer.WriteEpisodeAsync(reference, "https://media.example.test/video");

        Assert.True(result.IsSuccess);
        string expectedPath = Path.Combine(_root.FullName, "tv", "Paradise (2025)", "Season 01", "Paradise - S01E05.strm");
        Assert.Equal(expectedPath, result.Value);
        Assert.True(File.Exists(result.Value));
    }

    [Fact]
    public async Task WriteEpisodeAsync_FormatsSeasonAndEpisodeNumbersWithTwoDigits()
    {
        FileSystemStrmWriter writer = CreateWriter();
        var reference = new EpisodeStrmReference("Show", 2020, 9, 3);

        Result<string> result = await writer.WriteEpisodeAsync(reference, "https://media.example.test/video");

        Assert.True(result.IsSuccess);
        Assert.Contains("Season 09", result.Value, StringComparison.Ordinal);
        Assert.EndsWith("Show - S09E03.strm", result.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteEpisodeAsync_FileContentIsTheSourceUrl()
    {
        FileSystemStrmWriter writer = CreateWriter();
        var reference = new EpisodeStrmReference("Show", 2020, 1, 1);

        Result<string> result = await writer.WriteEpisodeAsync(reference, "https://media.example.test/video-token-abc");

        string content = await File.ReadAllTextAsync(result.Value);
        Assert.Equal("https://media.example.test/video-token-abc", content);
    }

    [Theory]
    [InlineData("Show: Subtitle", "Show_ Subtitle")]
    [InlineData("Show / Part", "Show _ Part")]
    [InlineData("Show?<>|", "Show____")]
    public async Task WriteEpisodeAsync_SanitizesInvalidFileNameCharactersInTitle(string rawTitle, string expectedSanitized)
    {
        FileSystemStrmWriter writer = CreateWriter();
        var reference = new EpisodeStrmReference(rawTitle, 2020, 1, 1);

        Result<string> result = await writer.WriteEpisodeAsync(reference, "https://media.example.test/video");

        Assert.True(result.IsSuccess);
        Assert.Contains(expectedSanitized, result.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteEpisodeAsync_TitleWithPathTraversalSequence_DoesNotEscapeRoot()
    {
        FileSystemStrmWriter writer = CreateWriter();
        var reference = new EpisodeStrmReference("../../evil", 2020, 1, 1);

        Result<string> result = await writer.WriteEpisodeAsync(reference, "https://media.example.test/video");

        Assert.True(result.IsSuccess);
        string fullPath = Path.GetFullPath(result.Value);
        Assert.StartsWith(Path.GetFullPath(_root.FullName), fullPath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteEpisodeAsync_TitleWithEmbeddedSlashes_DoesNotCreateNestedDirectories()
    {
        FileSystemStrmWriter writer = CreateWriter();
        var reference = new EpisodeStrmReference("Show/../../Name", 2020, 1, 1);

        Result<string> result = await writer.WriteEpisodeAsync(reference, "https://media.example.test/video");

        Assert.True(result.IsSuccess);
        // Exactly "tv/{series}/Season 01/{file}" deep - no extra directory levels
        // introduced by slashes embedded in the title.
        string relative = Path.GetRelativePath(_root.FullName, result.Value);
        Assert.Equal(4, relative.Split(Path.DirectorySeparatorChar).Length);
    }

    [Fact]
    public async Task WriteEpisodeAsync_CalledTwice_OverwritesExistingFileAtomically()
    {
        FileSystemStrmWriter writer = CreateWriter();
        var reference = new EpisodeStrmReference("Show", 2020, 1, 1);

        await writer.WriteEpisodeAsync(reference, "https://media.example.test/first");
        Result<string> secondResult = await writer.WriteEpisodeAsync(reference, "https://media.example.test/second");

        Assert.True(secondResult.IsSuccess);
        string content = await File.ReadAllTextAsync(secondResult.Value);
        Assert.Equal("https://media.example.test/second", content);

        // No leftover temp files after the atomic rename.
        string[] leftoverTempFiles = Directory.GetFiles(Path.GetDirectoryName(secondResult.Value)!, "*.tmp");
        Assert.Empty(leftoverTempFiles);
    }

    public void Dispose()
    {
        if (_root.Exists)
        {
            _root.Delete(recursive: true);
        }
    }
}
