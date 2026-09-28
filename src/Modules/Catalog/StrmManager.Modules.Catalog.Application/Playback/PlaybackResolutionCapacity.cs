using Microsoft.Extensions.Options;

namespace StrmManager.Modules.Catalog.Application.Playback;

/// <summary>
/// Global admission capacity shared by every just-in-time playback resolution,
/// regardless of media type.
///
/// A held lease represents one underlying provider/ffprobe resolution.
/// Waiters joining an existing single-flight resolution never acquire another
/// lease.
/// </summary>
public sealed class PlaybackResolutionCapacity : IDisposable
{
    private readonly SemaphoreSlim _slots;

    public PlaybackResolutionCapacity(
        IOptions<PlaybackResolutionOptions> options)
    {
        int maxConcurrentResolutions =
            options.Value.MaxConcurrentResolutions;

        _slots = new SemaphoreSlim(
            maxConcurrentResolutions,
            maxConcurrentResolutions);
    }

    public bool TryAcquireImmediately() =>
        _slots.Wait(0);

    public async Task AcquireAsync(
        CancellationToken cancellationToken)
    {
        await _slots.WaitAsync(cancellationToken);
    }

    public void Release() =>
        _slots.Release();

    public void Dispose() =>
        _slots.Dispose();
}
