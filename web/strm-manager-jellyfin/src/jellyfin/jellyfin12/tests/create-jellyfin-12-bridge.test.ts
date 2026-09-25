import { describe, expect, it, vi } from 'vitest';
import { createJellyfin12Bridge } from '../create-jellyfin-12-bridge';
import type { Jellyfin12Globals } from '../jellyfin-12-globals';

describe('createJellyfin12Bridge', () => {
    it('creates a normalized bridge from Jellyfin 12 globals', async () => {
        const navigate = vi.fn();

        const globals: Jellyfin12Globals = {
            ApiClient: {
                getCurrentUserId: () => 'user-1',
                getSystemInfo: async () => ({
                    Version: '12.1.0',
                    ProductName: 'Jellyfin Server',
                }),
            },
            Dashboard: {
                navigate,
            },
        };

        const bridge = await createJellyfin12Bridge(globals);

        expect(bridge.version).toEqual({
            major: 12,
            minor: 1,
            patch: 0,
            raw: '12.1.0',
        });

        expect(bridge.auth.getCurrentUserId()).toBe('user-1');

        expect(bridge.capabilities).toEqual({
            authenticatedUser: true,
            navigation: true,
            itemDetails: false,
            playback: false,
            homeIntegration: false,
        });

        await bridge.navigation.openHome();

        expect(navigate).toHaveBeenCalledOnce();
        expect(navigate).toHaveBeenCalledWith('#/home');
    });

    it('reports an anonymous user', async () => {
        const bridge = await createJellyfin12Bridge({
            ApiClient: {
                getCurrentUserId: () => null,
                getSystemInfo: async () => ({
                    Version: '12.1.0',
                }),
            },
        });

        expect(bridge.capabilities.authenticatedUser).toBe(false);
        expect(bridge.capabilities.navigation).toBe(false);
    });

    it('fails when ApiClient is unavailable', async () => {
        await expect(createJellyfin12Bridge({}))
            .rejects
            .toThrow('Jellyfin ApiClient is not available.');
    });

    it('fails when Jellyfin does not expose its version', async () => {
        await expect(createJellyfin12Bridge({
            ApiClient: {
                getCurrentUserId: () => 'user-1',
                getSystemInfo: async () => ({}),
            },
        }))
            .rejects
            .toThrow('Jellyfin server version is not available.');
    });

    it('does not claim unsupported item navigation', async () => {
        const bridge = await createJellyfin12Bridge({
            ApiClient: {
                getCurrentUserId: () => 'user-1',
                getSystemInfo: async () => ({
                    Version: '12.1.0',
                }),
            },
        });

        await expect(
            bridge.navigation.openItem({ id: 'item-1' }),
        ).rejects.toThrow(
            'Jellyfin item navigation is not implemented.',
        );
    });
});