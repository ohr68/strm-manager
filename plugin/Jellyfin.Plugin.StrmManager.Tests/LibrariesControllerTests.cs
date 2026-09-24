using Jellyfin.Plugin.StrmManager.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.StrmManager.Tests;

/// <summary>
/// Focused unit tests for LibrariesController's pure logic only. ILibraryManager.GetItemList's own query (which
/// items GetItemList returns) and QueueLibraryScan are NOT unit-tested here - ILibraryManager is far too large an
/// interface to meaningfully stub for that (same reasoning already established for GetLibraries/ProjectLibraries).
/// BuildFindMovieResult (UI-7.1a) is the exception: it is pure decision logic over an already-fetched item list and
/// a refresh-queue set, so it is tested directly with plain BaseItem/Movie instances - no ILibraryManager/
/// IProviderManager stubbing needed. This is a deliberate coverage boundary, not a gap.
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

    [Fact]
    public void BuildFindMovieResult_NoMatches_Returns404()
    {
        var result = LibrariesController.BuildFindMovieResult([], new HashSet<Guid>());

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public void BuildFindMovieResult_OneMatch_StillPendingMetadataRefresh_Returns404()
    {
        var movieId = Guid.NewGuid();
        BaseItem movie = new Movie { Id = movieId };

        var result = LibrariesController.BuildFindMovieResult([movie], new HashSet<Guid> { movieId });

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public void BuildFindMovieResult_OneMatch_NotInRefreshQueue_Returns200WithItemId()
    {
        var movieId = Guid.NewGuid();
        BaseItem movie = new Movie { Id = movieId };

        var result = LibrariesController.BuildFindMovieResult([movie], new HashSet<Guid>());

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(movieId, ok.Value!.GetType().GetProperty("itemId")!.GetValue(ok.Value));
    }

    [Fact]
    public void BuildFindMovieResult_MultipleMatches_Returns409_RegardlessOfRefreshQueue()
    {
        BaseItem first = new Movie { Id = Guid.NewGuid() };
        BaseItem second = new Movie { Id = Guid.NewGuid() };

        var result = LibrariesController.BuildFindMovieResult([first, second], new HashSet<Guid>());

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(409, conflict.StatusCode);
    }
}
