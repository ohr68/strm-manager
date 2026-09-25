import type {
    JellyfinBridge,
    JellyfinItemRef,
} from '../contracts/jellyfin-bridge';
import {
    getJellyfin12Globals,
    type Jellyfin12Globals,
} from './jellyfin-12-globals';
import { parseJellyfinVersion } from './parse-jellyfin-version';

export async function createJellyfin12Bridge(
    globals: Jellyfin12Globals = getJellyfin12Globals(),
): Promise<JellyfinBridge> {
    const apiClient = globals.ApiClient;

    if (!apiClient) {
        throw new Error('Jellyfin ApiClient is not available.');
    }

    const systemInfo = await apiClient.getSystemInfo();

    if (!systemInfo.Version) {
        throw new Error('Jellyfin server version is not available.');
    }

    const version = parseJellyfinVersion(systemInfo.Version);

    if (version.major !== 12) {
        throw new Error(
            `Unsupported Jellyfin major version for Jellyfin 12 adapter: ${version.raw}`,
        );
    }

    const dashboard = globals.Dashboard;

    return {
        version,

        capabilities: {
            navigation:
                typeof dashboard?.navigate === 'function',
            itemDetails: false,
            playback: false,
            homeIntegration: false,
        },

        auth: {
            getCurrentUserId(): string | null {
                return apiClient.getCurrentUserId();
            },
            isAuthenticated(): boolean {
                return apiClient.getCurrentUserId() !== null;
            },
        },

        navigation: {
            async openItem(item: JellyfinItemRef): Promise<void> {
                if (typeof dashboard?.navigate !== 'function') {
                    throw new Error(
                        'Jellyfin navigation is not available.',
                    );
                }

                const serverId = apiClient.serverInfo()?.Id;

                if (!serverId) {
                    throw new Error(
                        'Jellyfin server ID is not available.',
                    );
                }

                const itemId = encodeURIComponent(item.id);
                const encodedServerId = encodeURIComponent(serverId);

                dashboard.navigate(
                    `#/details?id=${itemId}&serverId=${encodedServerId}`,
                );
            },
            async openHome(): Promise<void> {
                if (!dashboard) {
                    throw new Error(
                        'Jellyfin Dashboard navigation is not available.',
                    );
                }

                dashboard.navigate('#/home');
            },
        },

        playback: {
            async play(_item: JellyfinItemRef): Promise<void> {
                throw new Error(
                    'Jellyfin playback is not implemented.',
                );
            },
        },
    };
}