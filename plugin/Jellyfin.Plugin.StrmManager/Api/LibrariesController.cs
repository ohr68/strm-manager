using System.Text.RegularExpressions;
using Jellyfin.Data.Enums;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using JfMetadataProvider = MediaBrowser.Model.Entities.MetadataProvider;

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
public sealed class LibrariesController(ILibraryManager libraryManager, IProviderManager providerManager) : ControllerBase
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

    /// <summary>
    /// UI-3a: lets the browser ask Jellyfin to rescan after a Movie reaches Completed (a real .strm now exists on
    /// disk). QueueLibraryScan() is whole-server, not scoped to just the configured Movies library - the same,
    /// already-proven mechanism the P0 spike used (ValidateMediaLibrary's narrower per-folder scoping was
    /// deliberately not researched for this slice). Fire-and-forget: does not wait for the scan to finish.
    /// </summary>
    [HttpPost("Libraries/Scan")]
    public IActionResult ScanLibraries()
    {
        libraryManager.QueueLibraryScan();
        return Accepted(new { queuedAtUtc = DateTime.UtcNow });
    }

    /// <summary>
    /// UI-3a: locates the real Jellyfin Movie item for an IMDb id, once a Completed .strm has been scanned in -
    /// the same GetItemList/HasAnyProviderId pattern the P0 spike already proved (see SpikeController.FindMovie),
    /// narrowed to exactly what UI-3 needs: an item id, or a clear "not found"/"ambiguous" outcome. The regex is
    /// the spike's own ("^tt\d{7,9}$"), reused rather than redefined, since that is the pattern this whole action
    /// is itself based on.
    /// </summary>
    [HttpGet("Libraries/Movies/By-Imdb/{imdbId}")]
    public IActionResult FindMovieByImdbId(string imdbId)
    {
        if (!IsValidImdbId(imdbId))
        {
            return BadRequest();
        }

        var query = new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Movie],
            Recursive = true,
            HasAnyProviderId = new Dictionary<string, string> { [JfMetadataProvider.Imdb.ToString()] = imdbId },
            DtoOptions = new DtoOptions(false),
        };

        IReadOnlyList<BaseItem> items = libraryManager.GetItemList(query);

        return BuildFindMovieResult(items, providerManager.GetRefreshQueue());
    }

    /// <summary>
    /// Pure decision logic for FindMovieByImdbId - kept separate and public so it can be unit-tested directly with
    /// plain BaseItem/Movie instances, without stubbing ILibraryManager/IProviderManager (same reasoning already
    /// established for ProjectLibraries/IsValidImdbId - those interfaces are too large to meaningfully stub).
    /// </summary>
    public static IActionResult BuildFindMovieResult(IReadOnlyList<BaseItem> items, IReadOnlySet<Guid> refreshQueue)
    {
        switch (items.Count)
        {
            case 0:
                return new NotFoundResult();
            case 1:
                // UI-7.1a: Jellyfin can expose a newly-discovered item before its own metadata refresh
                // (title/overview/images) has finished - the client already treats 404 as "not ready yet" and
                // keeps polling, so a match still pending refresh is reported the same way as no match at all.
                if (refreshQueue.Contains(items[0].Id))
                {
                    return new NotFoundResult();
                }

                return new OkObjectResult(new { itemId = items[0].Id });
            default:
                // GetItemList's result ordering with no explicit sort is not something this slice verified as stable -
                // reporting the ambiguity is safer than guessing a "first" result and silently picking the wrong movie.
                return new ConflictObjectResult(new { reason = "Multiple Jellyfin items matched this IMDb id." });
        }
    }

    private static readonly Regex ImdbIdPattern = new(@"^tt\d{7,9}$", RegexOptions.Compiled);

    /// <summary>Public so it can be unit-tested directly - see FindMovieByImdbId's remarks on why GetItemList itself is not.</summary>
    public static bool IsValidImdbId(string imdbId) => ImdbIdPattern.IsMatch(imdbId);
}

/// <summary>
/// One Jellyfin library as offered to the STRM Manager configuration page's Movies/Series dropdowns. ItemId is
/// VirtualFolderInfo's own stable string id (see PluginConfiguration.MoviesLibraryId/SeriesLibraryId) -
/// CollectionType is the library's declared type (e.g. "movies", "tvshows"), null for a library with none set, used
/// client-side only to filter which dropdown a library appears in.
/// </summary>
public sealed record LibraryOption(string ItemId, string Name, string? CollectionType);
