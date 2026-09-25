import {
    afterEach,
    describe,
    expect,
    it,
    vi,
} from 'vitest';
import {
    waitForJellyfin12HomeHost,
} from '../wait-for-jellyfin-12-home-host';
import {
    resolveJellyfin12HomeHost,
} from '../resolve-jellyfin-12-home-host';

vi.mock('../resolve-jellyfin-12-home-host', () => ({
    resolveJellyfin12HomeHost: vi.fn(),
}));

const mockedResolveHomeHost =
    vi.mocked(resolveJellyfin12HomeHost);

describe('waitForJellyfin12HomeHost', () => {
    afterEach(() => {
        vi.useRealTimers();
        vi.clearAllMocks();
    });

    it('returns immediately when the home host is available', async () => {
        const host = {
            container: {} as HTMLElement,
            firstSection: null,
        };

        mockedResolveHomeHost.mockReturnValue(host);

        await expect(
            waitForJellyfin12HomeHost(
                {} as Document,
                100,
                10,
            ),
        ).resolves.toBe(host);
    });

    it('waits until the home host becomes available', async () => {
        vi.useFakeTimers();

        const host = {
            container: {} as HTMLElement,
            firstSection: null,
        };

        mockedResolveHomeHost
            .mockReturnValueOnce(null)
            .mockReturnValue(host);

        const resultPromise =
            waitForJellyfin12HomeHost(
                {} as Document,
                100,
                10,
            );

        await vi.advanceTimersByTimeAsync(10);

        await expect(resultPromise).resolves.toBe(host);
    });

    it('returns null after the timeout', async () => {
        vi.useFakeTimers();

        mockedResolveHomeHost.mockReturnValue(null);

        const resultPromise =
            waitForJellyfin12HomeHost(
                {} as Document,
                20,
                10,
            );

        await vi.advanceTimersByTimeAsync(20);

        await expect(resultPromise).resolves.toBeNull();
    });
});
