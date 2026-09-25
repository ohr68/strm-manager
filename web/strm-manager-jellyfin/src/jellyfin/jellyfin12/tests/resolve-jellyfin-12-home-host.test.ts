import { describe, expect, it, vi } from 'vitest';
import { resolveJellyfin12HomeHost } from '../resolve-jellyfin-12-home-host';

function createDocument(options?: {
    homeAvailable?: boolean;
    homeActive?: boolean;
    containerAvailable?: boolean;
}) {
    const {
        homeAvailable = true,
        homeActive = true,
        containerAvailable = true,
    } = options ?? {};

    const firstSection = {} as Element;

    const container = {
        firstElementChild: firstSection,
    } as HTMLElement;

    const homeTab = {
        classList: {
            contains: vi.fn(
                (className: string) =>
                    className === 'is-active' && homeActive,
            ),
        },
        querySelector: vi.fn(
            () => containerAvailable ? container : null,
        ),
    } as unknown as Element;

    const indexPage = {
        querySelector: vi.fn(
            () => homeAvailable ? homeTab : null,
        ),
    } as unknown as Element;

    const documentRoot = {
        querySelector: vi.fn(
            () => homeAvailable ? indexPage : null,
        ),
    } as unknown as Document;

    return {
        documentRoot,
        container,
        firstSection,
    };
}

describe('resolveJellyfin12HomeHost', () => {
    it('resolves the active Jellyfin home sections container', () => {
        const {
            documentRoot,
            container,
            firstSection,
        } = createDocument();

        const result =
            resolveJellyfin12HomeHost(documentRoot);

        expect(result).toEqual({
            container,
            firstSection,
        });
    });

    it('returns null when the Jellyfin home page is unavailable', () => {
        const { documentRoot } = createDocument({
            homeAvailable: false,
        });

        expect(
            resolveJellyfin12HomeHost(documentRoot),
        ).toBeNull();
    });

    it('returns null when the home tab is inactive', () => {
        const { documentRoot } = createDocument({
            homeActive: false,
        });

        expect(
            resolveJellyfin12HomeHost(documentRoot),
        ).toBeNull();
    });

    it('returns null when the sections container is unavailable', () => {
        const { documentRoot } = createDocument({
            containerAvailable: false,
        });

        expect(
            resolveJellyfin12HomeHost(documentRoot),
        ).toBeNull();
    });
});
