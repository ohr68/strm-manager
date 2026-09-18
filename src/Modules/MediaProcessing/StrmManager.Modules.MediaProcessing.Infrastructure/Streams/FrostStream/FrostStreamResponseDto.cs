namespace StrmManager.Modules.MediaProcessing.Infrastructure.Streams.FrostStream;

/// <summary>Raw FrostStream/Stremio addon response shape (GET /stream/series/{episodeId}.json). Never exposed outside Infrastructure.</summary>
internal sealed class FrostStreamResponseDto
{
    public List<FrostStreamStreamDto>? Streams { get; set; }
}

internal sealed class FrostStreamStreamDto
{
    public string? Name { get; set; }

    public string? Title { get; set; }

    public string? Url { get; set; }
}
