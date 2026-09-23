using Jellyfin.Plugin.StrmManager.Api;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.StrmManager.Tests;

/// <summary>
/// Focused unit tests for LibrariesController.ProjectLibraries - the only real logic UI-1's library endpoint has.
/// Tested directly against real VirtualFolderInfo instances (a plain model type, trivially constructible) rather
/// than through the controller action, since faking ILibraryManager itself for one call would be disproportionate.
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
}
