using System.Text.Json;

namespace StrmManager.Modules.MediaProcessing.Infrastructure.Streams.FrostStream;

/// <summary>Raw FrostStream/Stremio addon response shape (GET /stream/{series|movie}/{id}.json). Never exposed outside Infrastructure.</summary>
internal sealed class FrostStreamResponseDto
{
    public List<FrostStreamStreamDto>? Streams { get; set; }
}

internal sealed class FrostStreamStreamDto
{
    public string? Name { get; set; }

    public string? Title { get; set; }

    public string? Url { get; set; }

    /// <summary>
    /// Kept as raw JSON on purpose: the mapper only asks "is anything here" (custom request
    /// headers are required) and never reads a value, and an unexpected shape must not be able
    /// to fail deserialisation of the whole lookup.
    /// </summary>
    public JsonElement? Headers { get; set; }

    /// <summary>Raw JSON for the same reason - only "bingeGroup", and only when it is a string, is ever looked at.</summary>
    public JsonElement? BehaviorHints { get; set; }
}
