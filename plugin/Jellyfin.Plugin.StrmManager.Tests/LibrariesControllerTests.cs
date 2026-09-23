using Jellyfin.Plugin.StrmManager.Api;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.StrmManager.Tests;

/// <summary>
/// Focused unit tests for LibrariesController's pure logic only. ILibraryManager.GetItemList (FindMovieByImdbId)
/// and QueueLibraryScan (ScanLibraries) are NOT unit-tested here - ILibraryManager is far too large an interface
/// to meaningfully stub for these actions (same reasoning already established for GetLibraries/ProjectLibraries).
/// This is a deliberate coverage boundary, verified only by a real Jellyfin smoke check, not a gap.
/// </summary>
public sealed class LibrariesControllerTests
{
    [Fact]
    public void ProjectLibraries_ReturnsItemIdNameAndCollectionType_ForEachVirtualFolder()
    {
        var movies = new VirtualFolderInfo { ItemId = "1", Name = "Movies", CollectionType = CollectionTypeOptions.movies };
        var series = new VirtualFolderInfo { ItemId = "2", Name = "Series", CollectionType = CollectionTypeOptions.tvshows };

        IReadOnlyList<LibraryOption> result = LibrariesController.ProjectLibraries([movies, series]);

        Assert.Collection(
            result,
            option => Assert.Equal(new LibraryOption("1", "Movies", "movies"), option),
            option => Assert.Equal(new LibraryOption("2", "Series", "tvshows"), option));
    }

    [Fact]
    public void ProjectLibraries_ALibraryWithNoCollectionType_ProjectsCollectionTypeAsNull()
    {
        var mixed = new VirtualFolderInfo { ItemId = "3", Name = "Everything", CollectionType = null };

        IReadOnlyList<LibraryOption> result = LibrariesController.ProjectLibraries([mixed]);

        Assert.Null(Assert.Single(result).CollectionType);
    }

    [Theory]
    [InlineData("tt0137523", true)]
    [InlineData("tt0111161", true)]
    [InlineData("tt12345678", true)]
    [InlineData("tt123", false)]
    [InlineData("not-an-imdb-id", false)]
    [InlineData("", false)]
    public void IsValidImdbId_MatchesTheEstablishedPattern(string imdbId, bool expected)
    {
        Assert.Equal(expected, LibrariesController.IsValidImdbId(imdbId));
    }
}
