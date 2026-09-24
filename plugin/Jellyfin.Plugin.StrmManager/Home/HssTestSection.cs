using Jellyfin.Data.Enums;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.StrmManager.Home;

/// <summary>
/// Temporary UI-9.6A section used only to prove the STRM Manager -> HSS
/// external-section integration before connecting the real catalog.
/// </summary>
public static class HssTestSection
{
    public static QueryResult<BaseItemDto> GetResults(
        Guid userId,
        string additionalData)
    {
        var item = new BaseItemDto
        {
            Id = Guid.Parse("96a00000-0000-0000-0000-000000000001"),
            Name = "STRM Manager Test",
            Type = BaseItemKind.Movie,
            ProductionYear = 2026,
        };

        return new QueryResult<BaseItemDto>
        {
            Items = [item],
            TotalRecordCount = 1,
        };
    }
}
