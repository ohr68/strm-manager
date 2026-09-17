using StrmManager.Common.Domain.Abstractions;

namespace StrmManager.Modules.Catalog.Domain.Seasons;

public sealed class Season : Entity
{
    private Season()
    {
    }

    public Guid Id { get; private set; }

    public Guid SeriesId { get; private set; }

    public int Number { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static Season Create(Guid seriesId, int number, DateTime utcNow) =>
        new()
        {
            Id = Guid.NewGuid(),
            SeriesId = seriesId,
            Number = number,
            CreatedAtUtc = utcNow,
            UpdatedAtUtc = utcNow,
        };
}
