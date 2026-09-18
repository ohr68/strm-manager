namespace StrmManager.Modules.Catalog.IntegrationTests.Infrastructure;

/// <summary>
/// Minimal controllable TimeProvider for deterministic release/retry/recovery/
/// metadata-refresh-boundary tests - never the real wall clock. Register it in place
/// of TimeProvider.System via factory.WithWebHostBuilder(...).
/// </summary>
public sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private DateTimeOffset _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void SetUtcNow(DateTimeOffset utcNow) => _utcNow = utcNow;

    public void Advance(TimeSpan delta) => _utcNow += delta;
}
