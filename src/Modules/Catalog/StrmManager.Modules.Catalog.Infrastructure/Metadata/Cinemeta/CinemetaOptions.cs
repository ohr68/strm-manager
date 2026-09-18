using System.ComponentModel.DataAnnotations;

namespace StrmManager.Modules.Catalog.Infrastructure.Metadata.Cinemeta;

public sealed class CinemetaOptions
{
    public const string SectionName = "Metadata:Cinemeta";

    /// <summary>
    /// The public Stremio Cinemeta addon. Not machine-specific - safe as a default,
    /// still overridable via configuration/environment variables.
    /// </summary>
    [Required]
    [Url]
    public string BaseUrl { get; set; } = "https://v3-cinemeta.strem.io/";

    [Range(1, 60)]
    public int TimeoutSeconds { get; set; } = 10;
}
