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
                serverInfo() {
                    return {
                        Id: 'server-1',
                    };
                },
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
        expect(bridge.auth.isAuthenticated()).toBe(true);

        expect(bridge.capabilities).toEqual({
            navigation: true,
            itemDetails: false,
            playback: false,
            homeIntegration: false,
        });

        await bridge.navigation.openHome();

        expect(navigate).toHaveBeenCalledOnce();
        expect(navigate).toHaveBeenCalledWith('#/home');
    });

    it('rejects home navigation when Jellyfin navigation is unavailable', async () => {
        const bridge = await createJellyfin12Bridge({
            ApiClient: {
                getCurrentUserId: () => 'user-1',
                getSystemInfo: async () => ({
                    Version: '12.1.0',
                }),
                serverInfo: () => ({
                    Id: 'server-1',
                }),
            },
        });

        await expect(
            bridge.navigation.openHome(),
        ).rejects.toThrow(
            'Jellyfin navigation is not available.',
        );
    });

    it('reports an anonymous user', async () => {
        const bridge = await createJellyfin12Bridge({
            ApiClient: {
                getCurrentUserId: () => null,
                getSystemInfo: async () => ({
                    Version: '12.1.0',
                }),
                serverInfo() {
                    return {
                        Id: 'server-1',
                    };
                },
            },
        });

        expect(bridge.auth.getCurrentUserId()).toBeNull();
        expect(bridge.auth.isAuthenticated()).toBe(false);
    });

    it('reads authentication state dynamically after bridge creation', async () => {
        let currentUserId: string | null = null;

        const bridge = await createJellyfin12Bridge({
            ApiClient: {
                getCurrentUserId: () => currentUserId,
                getSystemInfo: async () => ({
                    Version: '12.1.0',
                }),
                serverInfo() {
                    return {
                        Id: 'server-1',
                    };
                },
            },
        });

        expect(bridge.auth.getCurrentUserId()).toBeNull();
        expect(bridge.auth.isAuthenticated()).toBe(false);

        currentUserId = 'user-1';

        expect(bridge.auth.getCurrentUserId()).toBe('user-1');
        expect(bridge.auth.isAuthenticated()).toBe(true);
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
                serverInfo() {
                    return {
                        Id: 'server-1',
                    };
                },
            },
        }))
            .rejects
            .toThrow('Jellyfin server version is not available.');
    });

    it('rejects item navigation when the server ID is unavailable', async () => {
        const bridge = await createJellyfin12Bridge({
            ApiClient: {
                getCurrentUserId: () => 'user-1',
                getSystemInfo: async () => ({
                    Version: '12.1.0',
                }),
                serverInfo: () => ({}),
            },
            Dashboard: {
                navigate: vi.fn(),
            },
        });

        await expect(
            bridge.navigation.openItem({
                id: 'movie-1',
            }),
        ).rejects.toThrow(
            'Jellyfin server ID is not available.',
        );
    });

    it('rejects item navigation when Jellyfin navigation is unavailable', async () => {
        const bridge = await createJellyfin12Bridge({
            ApiClient: {
                getCurrentUserId: () => 'user-1',
                getSystemInfo: async () => ({
                    Version: '12.1.0',
                }),
                serverInfo: () => ({
                    Id: 'server-1',
                }),
            },
        });

        await expect(
            bridge.navigation.openItem({
                id: 'movie-1',
            }),
        ).rejects.toThrow(
            'Jellyfin navigation is not available.',
        );
    });

    it('rejects a Jellyfin version outside major 12', async () => {
      await expect(createJellyfin12Bridge({
          ApiClient: {
              getCurrentUserId: () => 'user-1',
              getSystemInfo: async () => ({
                  Version: '13.0.0',
              }),
              serverInfo() {
                return {
                    Id: 'server-1',
                };
              },
          },
      }))
          .rejects
          .toThrow(
              'Unsupported Jellyfin major version for Jellyfin 12 adapter: 13.0.0',
          );
    });

    it('navigates to the native Jellyfin item details route', async () => {
        const navigate = vi.fn();

        const bridge = await createJellyfin12Bridge({
            ApiClient: {
                getCurrentUserId: () => 'user-1',
                getSystemInfo: async () => ({
                    Version: '12.1.0',
                }),
                serverInfo: () => ({
                    Id: 'server-1',
                }),
            },
            Dashboard: {
                navigate,
            },
        });

        await bridge.navigation.openItem({
            id: 'movie-1',
        });

        expect(navigate).toHaveBeenCalledOnce();
        expect(navigate).toHaveBeenCalledWith(
            '#/details?id=movie-1&serverId=server-1',
        );
    });

    it('encodes item and server IDs in the details route', async () => {
        const navigate = vi.fn();

        const bridge = await createJellyfin12Bridge({
            ApiClient: {
                getCurrentUserId: () => 'user-1',
                getSystemInfo: async () => ({
                    Version: '12.1.0',
                }),
                serverInfo: () => ({
                    Id: 'server/id & 1',
                }),
            },
            Dashboard: {
                navigate,
            },
        });

        await bridge.navigation.openItem({
            id: 'movie/id & 1',
        });

        expect(navigate).toHaveBeenCalledWith(
            '#/details?id=movie%2Fid%20%26%201&serverId=server%2Fid%20%26%201',
        );
    });

    it('reports playback capability when the native playback manager is available', async () => {
        const play = vi.fn();

        const bridge = await createJellyfin12Bridge({
            ApiClient: {
                getCurrentUserId: () => 'user-1',
                getSystemInfo: async () => ({
                    Version: '12.1.0',
                }),
                serverInfo: () => ({
                    Id: 'server-1',
                }),
            },
            webpackChunk: {
                push(entry) {
                    entry[2]((moduleId) => {
                        expect(moduleId).toBe(68221);

                        return {
                            f: {
                                play,
                            },
                        };
                    });

                    return undefined;
                },
            },
        });

        expect(bridge.capabilities.playback).toBe(true);
    });

    it('plays an item through the native Jellyfin playback manager', async () => {
        const play = vi.fn().mockResolvedValue(undefined);

        const bridge = await createJellyfin12Bridge({
            ApiClient: {
                getCurrentUserId: () => 'user-1',
                getSystemInfo: async () => ({
                    Version: '12.1.0',
                }),
                serverInfo: () => ({
                    Id: 'server-1',
                }),
            },
            webpackChunk: {
                push(entry) {
                    entry[2](() => ({
                        f: {
                            play,
                        },
                    }));

                    return undefined;
                },
            },
        });

        await bridge.playback.play({
            id: 'movie-1',
        });

        expect(play).toHaveBeenCalledOnce();
        expect(play).toHaveBeenCalledWith({
            ids: ['movie-1'],
            serverId: 'server-1',
            fullscreen: true,
        });
    });

    it('rejects playback when the native playback manager is unavailable', async () => {
        const bridge = await createJellyfin12Bridge({
            ApiClient: {
                getCurrentUserId: () => 'user-1',
                getSystemInfo: async () => ({
                    Version: '12.1.0',
                }),
                serverInfo: () => ({
                    Id: 'server-1',
                }),
            },
        });

        expect(bridge.capabilities.playback).toBe(false);

        await expect(
            bridge.playback.play({
                id: 'movie-1',
            }),
        ).rejects.toThrow(
            'Jellyfin playback is not available.',
        );
    });

    it('rejects playback when the server ID is unavailable', async () => {
        const play = vi.fn();

        const bridge = await createJellyfin12Bridge({
            ApiClient: {
                getCurrentUserId: () => 'user-1',
                getSystemInfo: async () => ({
                    Version: '12.1.0',
                }),
                serverInfo: () => ({}),
            },
            webpackChunk: {
                push(entry) {
                    entry[2](() => ({
                        f: {
                            play,
                        },
                    }));

                    return undefined;
                },
            },
        });

        expect(bridge.capabilities.playback).toBe(true);

        await expect(
            bridge.playback.play({
                id: 'movie-1',
            }),
        ).rejects.toThrow(
            'Jellyfin server ID is not available.',
        );

        expect(play).not.toHaveBeenCalled();
    });

});
