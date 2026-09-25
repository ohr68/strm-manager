export interface JellyfinEnvironment {
    readonly browser: boolean;
    readonly reactRootPresent: boolean;
    readonly baseUrl: string;
    readonly path: string;
}

export function inspectJellyfinEnvironment(): JellyfinEnvironment {
    const browser =
        typeof window !== 'undefined' &&
        typeof document !== 'undefined';

    if (!browser) {
        return {
            browser: false,
            reactRootPresent: false,
            baseUrl: '',
            path: '',
        };
    }

    return {
        browser: true,
        reactRootPresent: document.getElementById('reactRoot') !== null,
        baseUrl: window.location.origin,
        path: window.location.pathname,
    };
}