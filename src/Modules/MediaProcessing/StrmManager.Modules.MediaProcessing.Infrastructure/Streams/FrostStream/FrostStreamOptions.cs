using System.ComponentModel.DataAnnotations;

namespace StrmManager.Modules.MediaProcessing.Infrastructure.Streams.FrostStream;

public sealed class FrostStreamOptions
{
    public const string SectionName = "StreamProviders:FrostStream";

    /// <summary>The Stremio-compatible FrostStream addon endpoint validated by the legacy prototype. Not a secret - still overridable.</summary>
    [Required]
    [Url]
    public string BaseUrl { get; set; } = "https://froststream.cloutteam.com/";

    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 15;
}
