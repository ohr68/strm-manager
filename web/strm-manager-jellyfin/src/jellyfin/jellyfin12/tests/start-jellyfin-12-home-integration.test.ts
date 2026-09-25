import {
    afterEach,
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
    startJellyfin12HomeIntegration,
} from '../start-jellyfin-12-home-integration';

vi.mock('../mount-jellyfin-12-home-integration', () => ({
    mountJellyfin12HomeIntegration: vi.fn(),
}));

const mockedMountHomeIntegration =
    vi.mocked(mountJellyfin12HomeIntegration);

function createFixture(options?: {
    reactRoot?: Element | null;
}) {
    const reactRoot =
        options?.reactRoot === undefined
            ? ({} as Element)
            : options.reactRoot;

    const documentRoot = {
        querySelector: vi.fn(() => reactRoot),
    } as unknown as Document;

    const integrationRoot =
        {} as HTMLElement;
    
    return {
        documentRoot,
        reactRoot,
        integrationRoot,
    };
}

describe('startJellyfin12HomeIntegration', () => {
    let observe: ReturnType<typeof vi.fn>;
    let disconnect: ReturnType<typeof vi.fn>;
    let observerCallback:
        MutationCallback | null;

    afterEach(() => {
        vi.unstubAllGlobals();
    });

    beforeEach(() => {
        vi.clearAllMocks();

        observe = vi.fn();
        disconnect = vi.fn();
        observerCallback = null;

        vi.stubGlobal(
            'MutationObserver',
            class {
                constructor(
                    callback: MutationCallback,
                ) {
                    observerCallback = callback;
                }

                observe = observe;
                disconnect = disconnect;
            },
        );
    });

    it('returns null when the Jellyfin React root is unavailable', () => {
        const fixture = createFixture({
            reactRoot: null,
        });

        const onMounted = vi.fn();

        expect(
            startJellyfin12HomeIntegration(  
                onMounted,
                fixture.documentRoot,
            ),
        ).toBeNull();

        expect(
            mockedMountHomeIntegration,
        ).not.toHaveBeenCalled();

        expect(onMounted).not.toHaveBeenCalled();
        expect(observe).not.toHaveBeenCalled();
    });

    it('reconciles immediately and observes the Jellyfin React root', () => {
        const fixture = createFixture();

        const onMounted = vi.fn();

        mockedMountHomeIntegration.mockReturnValue({
            root: fixture.integrationRoot,
            unmount: vi.fn(),
        });

        const result =
            startJellyfin12HomeIntegration(
                onMounted,
                fixture.documentRoot,
            );

        expect(onMounted).toHaveBeenCalledOnce();
        expect(onMounted).toHaveBeenCalledWith(
            fixture.integrationRoot,
        );
        
        expect(result).not.toBeNull();

        expect(
            mockedMountHomeIntegration,
        ).toHaveBeenCalledOnce();

        expect(
            mockedMountHomeIntegration,
        ).toHaveBeenCalledWith(
            fixture.documentRoot,
        );

        expect(observe).toHaveBeenCalledWith(
            fixture.reactRoot,
            {
                childList: true,
                subtree: true,
            },
        );
    });

    it('reconciles when Jellyfin changes the React tree', () => {
        const fixture = createFixture();

        const onMounted = vi.fn();

        mockedMountHomeIntegration.mockReturnValue({
            root: fixture.integrationRoot,
            unmount: vi.fn(),
        });

        startJellyfin12HomeIntegration(
            onMounted,
            fixture.documentRoot,
        );

        observerCallback?.(
            [],
            {} as MutationObserver,
        );

        expect(
            mockedMountHomeIntegration,
        ).toHaveBeenCalledTimes(2);

        expect(onMounted).toHaveBeenCalledOnce();
    });

    it('disconnects the observer when stopped', () => {
        const fixture = createFixture();
        
        const onMounted = vi.fn();

        const result =
            startJellyfin12HomeIntegration(
                onMounted,
                fixture.documentRoot,
            );

        result?.stop();

        expect(disconnect).toHaveBeenCalledOnce();
    });

    it('notifies when Jellyfin creates a new home integration root', () => {
        const fixture = createFixture();

        const firstRoot =
            {} as HTMLElement;

        const secondRoot =
            {} as HTMLElement;

        const onMounted = vi.fn();

        mockedMountHomeIntegration
            .mockReturnValueOnce({
                root: firstRoot,
                unmount: vi.fn(),
            })
            .mockReturnValueOnce(null)
            .mockReturnValueOnce({
                root: secondRoot,
                unmount: vi.fn(),
            });

        startJellyfin12HomeIntegration(
            onMounted,
            fixture.documentRoot,
        );

        observerCallback?.(
            [],
            {} as MutationObserver,
        );

        observerCallback?.(
            [],
            {} as MutationObserver,
        );

        expect(onMounted).toHaveBeenCalledTimes(2);

        expect(onMounted).toHaveBeenNthCalledWith(
            1,
            firstRoot,
        );

        expect(onMounted).toHaveBeenNthCalledWith(
            2,
            secondRoot,
        );
    });
});
