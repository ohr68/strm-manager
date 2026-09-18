using StrmManager.Modules.Catalog.Domain.Shared;
using SeriesEntity = StrmManager.Modules.Catalog.Domain.Series.Series;

namespace StrmManager.Modules.Catalog.UnitTests.Series;

public class SeriesTests
{
    private static readonly DateTime UtcNow = new(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc);

    private static SeriesEntity CreateSeries() =>
        SeriesEntity.Create(new ExternalIds("tt27497393", null, null), "Stuart Fails to Save the Universe", null, 2026, UtcNow);

    [Fact]
    public void MarkMetadataRefreshAttempted_SetsLastAndNextRefreshTimestamps()
    {
        SeriesEntity series = CreateSeries();
        DateTime attemptedAtUtc = UtcNow.AddHours(1);
        DateTime nextRefreshAtUtc = attemptedAtUtc.AddHours(6);

        series.MarkMetadataRefreshAttempted(attemptedAtUtc, nextRefreshAtUtc);

        Assert.Equal(attemptedAtUtc, series.LastMetadataRefreshAtUtc);
        Assert.Equal(nextRefreshAtUtc, series.NextMetadataRefreshAtUtc);
        Assert.Equal(attemptedAtUtc, series.UpdatedAtUtc);
    }

    [Fact]
    public void MarkMetadataRefreshAttempted_CalledAgainAfterAFailure_AdvancesTheSchedule()
    {
        // Mirrors what RefreshSeriesMetadataCommandHandler does on a provider failure -
        // the schedule must still move forward, never stay stuck retrying immediately.
        SeriesEntity series = CreateSeries();
        series.MarkMetadataRefreshAttempted(UtcNow, UtcNow.AddHours(6));

        DateTime secondAttemptUtc = UtcNow.AddHours(6);
        series.MarkMetadataRefreshAttempted(secondAttemptUtc, secondAttemptUtc.AddHours(6));

        Assert.Equal(secondAttemptUtc, series.LastMetadataRefreshAtUtc);
        Assert.Equal(secondAttemptUtc.AddHours(6), series.NextMetadataRefreshAtUtc);
    }
}
