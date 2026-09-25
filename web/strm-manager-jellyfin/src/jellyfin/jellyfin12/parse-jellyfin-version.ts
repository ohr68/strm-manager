import type { JellyfinVersion } from '../contracts/jellyfin-bridge';

export function parseJellyfinVersion(raw: string): JellyfinVersion {
    const normalized = raw.trim();
    const match = /^(\d+)\.(\d+)\.(\d+)(?:[.-].*)?$/.exec(normalized);

    if (!match) {
        throw new Error(`Unsupported Jellyfin version: ${raw}`);
    }

    return {
        major: Number(match[1]),
        minor: Number(match[2]),
        patch: Number(match[3]),
        raw: normalized,
    };
}