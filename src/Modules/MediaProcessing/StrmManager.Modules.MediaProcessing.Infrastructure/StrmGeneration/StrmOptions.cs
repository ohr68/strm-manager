using System.ComponentModel.DataAnnotations;

namespace StrmManager.Modules.MediaProcessing.Infrastructure.StrmGeneration;

public sealed class StrmOptions
{
    public const string SectionName = "Strm";

    [Required]
    public string RootPath { get; set; } = "/stream";
}
