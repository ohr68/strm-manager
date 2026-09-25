import { describe, expect, it, vi } from 'vitest';
import {
    resolveJellyfin12PlaybackManager,
    type Jellyfin12WebpackGlobals,
} from '../resolve-jellyfin-12-playback-manager';

describe('resolveJellyfin12PlaybackManager', () => {
    it('returns null when the webpack runtime is unavailable', () => {
        expect(
            resolveJellyfin12PlaybackManager({}),
        ).toBeNull();
    });

    it('returns null when the playback module cannot be loaded', () => {
        const globals = createWebpackGlobals(() => {
            throw new Error('Module unavailable.');
        });

        expect(
            resolveJellyfin12PlaybackManager(globals),
        ).toBeNull();
    });

    it('returns null when the playback module has no manager export', () => {
        const globals = createWebpackGlobals(() => ({}));

        expect(
            resolveJellyfin12PlaybackManager(globals),
        ).toBeNull();
    });

    it('returns null when the playback manager has no play function', () => {
        const globals = createWebpackGlobals(() => ({
            f: {},
        }));

        expect(
            resolveJellyfin12PlaybackManager(globals),
        ).toBeNull();
    });

    it('resolves the Jellyfin playback manager', () => {
        const play = vi.fn();

        const globals = createWebpackGlobals((moduleId) => {
            expect(moduleId).toBe(68221);

            return {
                f: {
                    play,
                },
            };
        });

        const manager = resolveJellyfin12PlaybackManager(globals);

        expect(manager).not.toBeNull();
        expect(manager?.play).toBe(play);
    });
});

function createWebpackGlobals(
    requireModule: (moduleId: number) => unknown,
): Jellyfin12WebpackGlobals {
    return {
        webpackChunk: {
            push(entry) {
                const runtime = entry[2];

                runtime(requireModule);

                return undefined;
            },
        },
    };
}
