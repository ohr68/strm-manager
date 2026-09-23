using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.StrmManager.Api;

/// <summary>
/// UI-1's configuration page needs to let an admin pick existing Jellyfin libraries instead of typing filesystem
/// paths - this exposes exactly what ILibraryManager.GetVirtualFolders() already has (the same call the P0 spike
/// proved works from a plugin controller), projected down to what the page needs. Admin-only, same policy as
/// MoviesController. Deliberately its own controller rather than added to MoviesController - this is
/// configuration/library support, not a movie operation.
/// </summary>
[ApiController]
[Route("StrmManager")]
[Authorize(Policy = Policies.RequiresElevation)]
public sealed class LibrariesController(ILibraryManager libraryManager) : ControllerBase
{
    [HttpGet("Libraries")]
    public ActionResult<IReadOnlyList<LibraryOption>> GetLibraries() =>
        Ok(ProjectLibraries(libraryManager.GetVirtualFolders()));

    /// <summary>
    /// Pure projection, kept separate from the action and public so it can be unit-tested directly against real
    /// VirtualFolderInfo instances - ILibraryManager itself is far too large an interface to meaningfully stub for
    /// one action, so the action body stays a one-line pass-through and all the actual logic lives here instead.
    /// </summary>
    public static IReadOnlyList<LibraryOption> ProjectLibraries(IEnumerable<VirtualFolderInfo> libraries) =>
        libraries
            .Select(library => new LibraryOption(library.ItemId, library.Name, library.CollectionType?.ToString()))
            .ToList();
}

/// <summary>
/// One Jellyfin library as offered to the STRM Manager configuration page's Movies/Series dropdowns. ItemId is
/// VirtualFolderInfo's own stable string id (see PluginConfiguration.MoviesLibraryId/SeriesLibraryId) -
/// CollectionType is the library's declared type (e.g. "movies", "tvshows"), null for a library with none set, used
/// client-side only to filter which dropdown a library appears in.
/// </summary>
public sealed record LibraryOption(string ItemId, string Name, string? CollectionType);
