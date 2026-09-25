import {
    getJellyfin12Globals,
    type Jellyfin12Globals,
} from './jellyfin-12-globals';

export interface WaitForJellyfin12GlobalsOptions {
    readonly timeoutMs?: number;
    readonly intervalMs?: number;
    readonly getGlobals?: () => Jellyfin12Globals;
}

export async function waitForJellyfin12Globals(
    options: WaitForJellyfin12GlobalsOptions = {},
): Promise<Jellyfin12Globals> {
    const timeoutMs = options.timeoutMs ?? 10_000;
    const intervalMs = options.intervalMs ?? 50;
    const getGlobals = options.getGlobals ?? getJellyfin12Globals;

    const startedAt = Date.now();

    while (Date.now() - startedAt < timeoutMs) {
        const globals = getGlobals();

        if (globals.ApiClient) {
            return globals;
        }

        await delay(intervalMs);
    }

    throw new Error(
        `Jellyfin ApiClient was not available within ${timeoutMs}ms.`,
    );
}

function delay(milliseconds: number): Promise<void> {
    return new Promise((resolve) => {
        setTimeout(resolve, milliseconds);
    });
}