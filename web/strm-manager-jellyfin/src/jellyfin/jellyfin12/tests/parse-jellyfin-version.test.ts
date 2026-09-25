import { describe, expect, it } from 'vitest';
import { parseJellyfinVersion } from '../parse-jellyfin-version';

describe('parseJellyfinVersion', () => {
    it('parses a stable Jellyfin version', () => {
        expect(parseJellyfinVersion('12.1.0')).toEqual({
            major: 12,
            minor: 1,
            patch: 0,
            raw: '12.1.0',
        });
    });

    it('normalizes surrounding whitespace', () => {
        expect(parseJellyfinVersion(' 12.1.0 ')).toEqual({
            major: 12,
            minor: 1,
            patch: 0,
            raw: '12.1.0',
        });
    });

    it('preserves a version suffix', () => {
        expect(parseJellyfinVersion('12.1.0-beta.1')).toEqual({
            major: 12,
            minor: 1,
            patch: 0,
            raw: '12.1.0-beta.1',
        });
    });

    it('rejects an invalid version', () => {
        expect(() => parseJellyfinVersion('unknown'))
            .toThrow('Unsupported Jellyfin version: unknown');
    });
});