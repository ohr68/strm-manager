using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.MediaProcessing.Application.StrmGeneration;
using StrmManager.Modules.MediaProcessing.Infrastructure.StrmGeneration;

namespace StrmManager.Modules.MediaProcessing.UnitTests.StrmGeneration;

/// <summary>
/// Movie half of FileSystemStrmWriter: movies/{Title} ({Year}) [imdbid-{ImdbId}]/{Title} ({Year}).strm,
/// through the same sanitization, root-containment and atomic-write code as episodes. The IMDb id in the
/// folder name is what keeps two different movies with the same title and year apart.
/// Every test uses its own temp directory - never the real /stream path.
/// </summary>
public sealed class FileSystemStrmWriterMovieTests : IDisposable
{
    private const string Url = "https://media.example.test/movie?token=URL-SECRET";

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("strm-manager-tests-");

    private FileSystemStrmWriter CreateWriter() =>
        new(Options.Create(new StrmOptions { RootPath = _root.FullName }), NullLogger<FileSystemStrmWriter>.Instance);

    [Fact]
    public async Task WriteMovieAsync_ProducesTheJellyfinCompatiblePathWithTheImdbIdInTheFolder()
    {
        Result<string> result = await CreateWriter().WriteMovieAsync(new MovieStrmReference("The Lion King", 1994, "tt0110357"), Url);

        Assert.True(result.IsSuccess);
        string expected = Path.Combine(_root.FullName, "movies", "The Lion King (1994) [imdbid-tt0110357]", "The Lion King (1994).strm");
        Assert.Equal(expected, result.Value);
        Assert.True(File.Exists(result.Value));
    }

    [Fact]
    public async Task WriteMovieAsync_FileContentIsExactlyTheSourceUrl()
    {
        Result<string> result = await CreateWriter().WriteMovieAsync(new MovieStrmReference("Movie", 2020, "tt0000001"), Url);

        Assert.Equal(Url, await File.ReadAllTextAsync(result.Value));
    }

    [Fact]
    public async Task WriteMovieAsync_TwoMoviesWithTheSameTitleAndYear_GetDifferentFolders()
    {
        FileSystemStrmWriter writer = CreateWriter();

        Result<string> first = await writer.WriteMovieAsync(new MovieStrmReference("Same Title", 2019, "tt0000001"), "https://media.example.test/first");
        Result<string> second = await writer.WriteMovieAsync(new MovieStrmReference("Same Title", 2019, "tt0000002"), "https://media.example.test/second");

        Assert.NotEqual(first.Value, second.Value);
        Assert.Equal("https://media.example.test/first", await File.ReadAllTextAsync(first.Value)); // nothing was overwritten
        Assert.Equal("https://media.example.test/second", await File.ReadAllTextAsync(second.Value));
    }

    [Theory]
    [InlineData("Mission: Impossible", "Mission_ Impossible")]
    [InlineData("Show / Part", "Show _ Part")]
    [InlineData("What?<>|", "What____")]
    [InlineData("Say \"Hi\"", "Say _Hi_")]
    public async Task WriteMovieAsync_SanitizesInvalidCharactersInTheTitle_InFolderAndFileName(string rawTitle, string sanitized)
    {
        Result<string> result = await CreateWriter().WriteMovieAsync(new MovieStrmReference(rawTitle, 1996, "tt0117060"), Url);

        Assert.True(result.IsSuccess);
        Assert.Equal(Path.Combine(_root.FullName, "movies", $"{sanitized} (1996) [imdbid-tt0117060]", $"{sanitized} (1996).strm"), result.Value);
    }

    [Theory]
    [InlineData("../../../etc/passwd")]
    [InlineData("..\\..\\Windows")]
    [InlineData("....//....//x")]
    [InlineData("/absolute/path")]
    public async Task WriteMovieAsync_HostileTitle_CannotEscapeTheMoviesFolderUnderTheRoot(string hostileTitle)
    {
        Result<string> result = await CreateWriter().WriteMovieAsync(new MovieStrmReference(hostileTitle, 2000, "tt0000003"), Url);

        Assert.True(result.IsSuccess);
        string moviesRoot = Path.Combine(_root.FullName, "movies") + Path.DirectorySeparatorChar;
        Assert.StartsWith(moviesRoot, Path.GetFullPath(result.Value), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("tt1/../../x")]
    [InlineData("..\\..\\x")]
    [InlineData("../../x")]
    public async Task WriteMovieAsync_HostileImdbId_CannotEscapeTheMoviesFolderUnderTheRoot(string hostileImdbId)
    {
        Result<string> result = await CreateWriter().WriteMovieAsync(new MovieStrmReference("Title", 2000, hostileImdbId), Url);

        Assert.True(result.IsSuccess);
        string moviesRoot = Path.Combine(_root.FullName, "movies") + Path.DirectorySeparatorChar;
        Assert.StartsWith(moviesRoot, Path.GetFullPath(result.Value), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteMovieAsync_WritingTwiceOverwritesAtomically_AndLeavesNoTempFile()
    {
        FileSystemStrmWriter writer = CreateWriter();
        var reference = new MovieStrmReference("Movie", 2020, "tt0000001");

        await writer.WriteMovieAsync(reference, "https://media.example.test/old");
        Result<string> second = await writer.WriteMovieAsync(reference, "https://media.example.test/new");

        Assert.Equal("https://media.example.test/new", await File.ReadAllTextAsync(second.Value));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(second.Value)!, "*.tmp", SearchOption.AllDirectories));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(second.Value)!));
    }

    [Fact]
    public async Task WriteMovieAsync_EpisodeAndMovieLayouts_LiveSideBySideUnderTheSameRoot()
    {
        FileSystemStrmWriter writer = CreateWriter();

        Result<string> movie = await writer.WriteMovieAsync(new MovieStrmReference("Title", 2020, "tt0000001"), Url);
        Result<string> episode = await writer.WriteEpisodeAsync(new EpisodeStrmReference("Title", 2020, 1, 1), Url);

        Assert.Contains($"{Path.DirectorySeparatorChar}movies{Path.DirectorySeparatorChar}", movie.Value, StringComparison.Ordinal);
        Assert.Contains($"{Path.DirectorySeparatorChar}tv{Path.DirectorySeparatorChar}", episode.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteMovieAsync_WhenTheFolderCannotBeCreated_FailsWithoutLeakingTheUrl()
    {
        // A regular FILE where the "movies" directory must go: CreateDirectory genuinely fails.
        await File.WriteAllTextAsync(Path.Combine(_root.FullName, "movies"), "not a directory");

        Result<string> result = await CreateWriter().WriteMovieAsync(new MovieStrmReference("Movie", 2020, "tt0000001"), Url);

        Assert.True(result.IsFailure);
        Assert.Equal("Strm.WriteFailed", result.Error.Code);
        Assert.DoesNotContain("media.example.test", result.Error.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("URL-SECRET", result.Error.Description, StringComparison.Ordinal);
    }

    public void Dispose() => _root.Delete(recursive: true);
}
