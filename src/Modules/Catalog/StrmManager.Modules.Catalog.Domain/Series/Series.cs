using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Domain.Shared;

namespace StrmManager.Modules.Catalog.Domain.Series;

public sealed class Series : Entity
{
    private Series()
    {
    }

    public Guid Id { get; private set; }

    public ExternalIds ExternalIds { get; private set; } = null!;

    public string Title { get; private set; } = string.Empty;

    public string? OriginalTitle { get; private set; }

    public int Year { get; private set; }

    public SeriesStatus Status { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static Series Create(
        ExternalIds externalIds,
        string title,
        string? originalTitle,
        int year,
        DateTime utcNow) =>
        new()
        {
            Id = Guid.NewGuid(),
            ExternalIds = externalIds,
            Title = title,
            OriginalTitle = originalTitle,
            Year = year,
            Status = SeriesStatus.Active,
            CreatedAtUtc = utcNow,
            UpdatedAtUtc = utcNow,
        };

    public void UpdateMetadata(string title, string? originalTitle, int year, SeriesStatus status, DateTime utcNow)
    {
        Title = title;
        OriginalTitle = originalTitle;
        Year = year;
        Status = status;
        UpdatedAtUtc = utcNow;
    }
}
