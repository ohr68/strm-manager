export interface Jellyfin12PlaybackOptions {
    readonly ids: readonly string[];
    readonly serverId: string;
    readonly fullscreen?: boolean;
}

export interface Jellyfin12PlaybackManager {
    play(options: Jellyfin12PlaybackOptions): Promise<unknown>;
}

interface JellyfinWebpackRequire {
    (moduleId: number): unknown;
}

type JellyfinWebpackChunkEntry = [
    readonly string[],
    Record<string, never>,
    (require: JellyfinWebpackRequire) => void,
];

interface JellyfinWebpackChunk {
    push(entry: JellyfinWebpackChunkEntry): unknown;
}

interface JellyfinPlaybackModule {
    readonly f?: unknown;
}

export interface Jellyfin12WebpackGlobals {
    readonly webpackChunk?: JellyfinWebpackChunk;
}

const JELLYFIN_12_PLAYBACK_MODULE_ID = 68221;

export function resolveJellyfin12PlaybackManager(
    globals: Jellyfin12WebpackGlobals,
): Jellyfin12PlaybackManager | null {
    const webpackChunk = globals.webpackChunk;

    if (!webpackChunk || typeof webpackChunk.push !== 'function') {
        return null;
    }

    const capturedRequire: {
        current?: JellyfinWebpackRequire;
    } = {};

    webpackChunk.push([
        [`strm_manager_${Date.now()}`],
        {},
        (require) => {
            capturedRequire.current = require;
        },
    ]);

    const webpackRequire = capturedRequire.current;

    if (!webpackRequire) {
        return null;
    }

    let module: unknown;

    try {
        module = webpackRequire(JELLYFIN_12_PLAYBACK_MODULE_ID);
    } catch {
        return null;
    }

    if (!isRecord(module)) {
        return null;
    }

    const playbackManager = (module as JellyfinPlaybackModule).f;

    if (!isRecord(playbackManager)) {
        return null;
    }

    if (typeof playbackManager.play !== 'function') {
        return null;
    }

    return playbackManager as unknown as Jellyfin12PlaybackManager;
}

function isRecord(
    value: unknown,
): value is Record<string, unknown> {
    return (
        value !== null &&
        (typeof value === 'object' ||
            typeof value === 'function')
    );
}
