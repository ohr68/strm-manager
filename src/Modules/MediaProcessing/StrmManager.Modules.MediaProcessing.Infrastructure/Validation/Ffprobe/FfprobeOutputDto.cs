using System.Text.Json.Serialization;

namespace StrmManager.Modules.MediaProcessing.Infrastructure.Validation.Ffprobe;

/// <summary>
/// Only the fields the validator actually needs - not ffprobe's full schema (no tags,
/// chapters, disposition details, etc.). ffprobe's JSON uses snake_case keys
/// (codec_type, codec_name), which JsonSerializerDefaults.Web's camelCase naming
/// policy does not match - hence the explicit JsonPropertyName attributes below.
/// </summary>
internal sealed class FfprobeOutputDto
{
    public List<FfprobeStreamDto>? Streams { get; set; }

    public FfprobeFormatDto? Format { get; set; }
}

internal sealed class FfprobeStreamDto
{
    [JsonPropertyName("codec_type")]
    public string? CodecType { get; set; }

    [JsonPropertyName("codec_name")]
    public string? CodecName { get; set; }
}

internal sealed class FfprobeFormatDto
{
    /// <summary>ffprobe reports this as a decimal string (e.g. "3003.880000"), not a number.</summary>
    public string? Duration { get; set; }
}
