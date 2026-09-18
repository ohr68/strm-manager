using System.ComponentModel.DataAnnotations;

namespace StrmManager.Modules.Catalog.Application.Metadata;

/// <summary>
/// How often an Active Series' metadata is automatically re-synchronized against its
/// provider (so newly announced episodes/seasons, corrected release dates, or a
/// series ending get picked up without a manual refresh). Ended series are never
/// refreshed automatically - see RunCatalogMaintenanceCommandHandler and ADR-013.
/// </summary>
public sealed class MetadataRefreshOptions
{
    public const string SectionName = "Metadata:Refresh";

    [Range(typeof(TimeSpan), "00:15:00", "30.00:00:00")]
    public TimeSpan ActiveSeriesRefreshInterval { get; set; } = TimeSpan.FromHours(6);
}
