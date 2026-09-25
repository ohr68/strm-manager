import {
    beforeEach,
    describe,
    expect,
    it,
    vi,
} from 'vitest';
import {
    mountJellyfin12HomeIntegration,
} from '../mount-jellyfin-12-home-integration';
import {
    resolveJellyfin12HomeHost,
} from '../resolve-jellyfin-12-home-host';

vi.mock('../resolve-jellyfin-12-home-host', () => ({
    resolveJellyfin12HomeHost: vi.fn(),
}));

const mockedResolveHomeHost =
    vi.mocked(resolveJellyfin12HomeHost);

function createFixture(options?: {
    existingRoot?: HTMLElement | null;
    firstSection?: Element | null;
}) {
    const existingRoot =
        options?.existingRoot ?? null;

    const firstSection =
        options?.firstSection === undefined
            ? ({} as Element)
            : options.firstSection;

    const remove = vi.fn();

    const root = {
        id: '',
        setAttribute: vi.fn(),
        remove,
    } as unknown as HTMLElement;

    const container = {
        insertBefore: vi.fn(),
        appendChild: vi.fn(),
    } as unknown as HTMLElement;

    const documentRoot = {
        querySelector: vi.fn(() => existingRoot),
        createElement: vi.fn(() => root),
    } as unknown as Document;

    return {
        documentRoot,
        container,
        firstSection,
        root,
        remove,
    };
}

describe('mountJellyfin12HomeIntegration', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    it('returns null when the Jellyfin home host is unavailable', () => {
        const { documentRoot } = createFixture();

        mockedResolveHomeHost.mockReturnValue(null);

        expect(
            mountJellyfin12HomeIntegration(documentRoot),
        ).toBeNull();
    });

    it('mounts before the first native Jellyfin section', () => {
        const fixture = createFixture();

        mockedResolveHomeHost.mockReturnValue({
            container: fixture.container,
            firstSection: fixture.firstSection,
        });

        const result =
            mountJellyfin12HomeIntegration(
                fixture.documentRoot,
            );

        expect(result?.root).toBe(fixture.root);

        expect(
            fixture.container.insertBefore,
        ).toHaveBeenCalledWith(
            fixture.root,
            fixture.firstSection,
        );

        expect(
            fixture.root.setAttribute,
        ).toHaveBeenCalledWith(
            'data-strm-manager-integration',
            'home',
        );
    });

    it('appends when the Jellyfin home has no native sections', () => {
        const fixture = createFixture({
            firstSection: null,
        });

        mockedResolveHomeHost.mockReturnValue({
            container: fixture.container,
            firstSection: null,
        });

        mountJellyfin12HomeIntegration(
            fixture.documentRoot,
        );

        expect(
            fixture.container.appendChild,
        ).toHaveBeenCalledWith(fixture.root);
    });

    it('reuses an existing integration root', () => {
        const existingRoot = {
            remove: vi.fn(),
        } as unknown as HTMLElement;

        const fixture = createFixture({
            existingRoot,
        });

        mockedResolveHomeHost.mockReturnValue({
            container: fixture.container,
            firstSection: fixture.firstSection,
        });

        const result =
            mountJellyfin12HomeIntegration(
                fixture.documentRoot,
            );

        expect(result?.root).toBe(existingRoot);

        expect(
            fixture.documentRoot.createElement,
        ).not.toHaveBeenCalled();

        expect(
            fixture.container.insertBefore,
        ).not.toHaveBeenCalled();
    });

    it('removes the integration root when unmounted', () => {
        const fixture = createFixture();

        mockedResolveHomeHost.mockReturnValue({
            container: fixture.container,
            firstSection: fixture.firstSection,
        });

        const result =
            mountJellyfin12HomeIntegration(
                fixture.documentRoot,
            );

        result?.unmount();

        expect(fixture.remove).toHaveBeenCalledOnce();
    });
});
