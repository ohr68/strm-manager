import {
    Jellyfin12HomeHost,
    resolveJellyfin12HomeHost,
} from './resolve-jellyfin-12-home-host';

const DEFAULT_TIMEOUT_MS = 10_000;
const DEFAULT_POLL_INTERVAL_MS = 50;

export async function waitForJellyfin12HomeHost(
    documentRoot: Document = document,
    timeoutMs = DEFAULT_TIMEOUT_MS,
    pollIntervalMs = DEFAULT_POLL_INTERVAL_MS,
): Promise<Jellyfin12HomeHost | null> {
    const deadline = Date.now() + timeoutMs;

    while (Date.now() < deadline) {
        const host =
            resolveJellyfin12HomeHost(documentRoot);

        if (host) {
            return host;
        }

        await new Promise<void>((resolve) => {
            setTimeout(resolve, pollIntervalMs);
        });
    }

    return resolveJellyfin12HomeHost(documentRoot);
}
