using System.ComponentModel.DataAnnotations;

namespace StrmManager.Modules.MediaProcessing.Infrastructure.Validation.Ffprobe;

public sealed class FfprobeOptions
{
    public const string SectionName = "MediaValidation:Ffprobe";

    /// <summary>Executable name/path. Invoked directly (ProcessStartInfo, no shell) - see ADR-009.</summary>
    [Required]
    public string ExecutablePath { get; set; } = "ffprobe";

    [Range(1, 300)]
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Legacy-validated tolerance: found duration may differ from the expected runtime by up to this percentage.</summary>
    [Range(0, 100)]
    public double EpisodeRuntimeTolerancePercentage { get; set; } = 35;

    /// <summary>Used only when ExpectedRuntime is unavailable - legacy-validated safety floor.</summary>
    [Range(0, 36000)]
    public int MinimumEpisodeDurationSeconds { get; set; } = 1200;
}
