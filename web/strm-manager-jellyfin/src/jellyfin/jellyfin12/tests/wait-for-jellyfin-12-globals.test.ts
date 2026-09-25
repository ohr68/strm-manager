import { describe, expect, it, vi } from 'vitest';
import { waitForJellyfin12Globals } from '../wait-for-jellyfin-12-globals';

describe('waitForJellyfin12Globals', () => {
    it('returns immediately when ApiClient is already available', async () => {
        const globals = {
            ApiClient: {
                getCurrentUserId: () => 'user-1',
                getSystemInfo: async () => ({
                    Version: '12.1.0',
                }),
            },
        };

        await expect(waitForJellyfin12Globals({
            getGlobals: () => globals,
        })).resolves.toBe(globals);
    });

    it('waits until ApiClient becomes available', async () => {
        vi.useFakeTimers();

        try {
            let available = false;

            const promise = waitForJellyfin12Globals({
                timeoutMs: 1_000,
                intervalMs: 50,
                getGlobals: () => available
                    ? {
                        ApiClient: {
                            getCurrentUserId: () => 'user-1',
                            getSystemInfo: async () => ({
                                Version: '12.1.0',
                            }),
                        },
                    }
                    : {},
            });

            available = true;

            await vi.advanceTimersByTimeAsync(50);

            await expect(promise).resolves.toBeDefined();
        } finally {
            vi.useRealTimers();
        }
    });

    it('fails after the readiness timeout', async () => {
        vi.useFakeTimers();

        try {
            const promise = waitForJellyfin12Globals({
                timeoutMs: 100,
                intervalMs: 25,
                getGlobals: () => ({}),
            });

            const expectation = expect(promise).rejects.toThrow(
                'Jellyfin ApiClient was not available within 100ms.',
            );

            await vi.advanceTimersByTimeAsync(125);

            await expectation;
        } finally {
            vi.useRealTimers();
        }
    });
});