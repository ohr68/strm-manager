import { inspectJellyfinEnvironment } from './jellyfin/capabilities/inspect-jellyfin-environment';
import { createJellyfin12Bridge } from './jellyfin/jellyfin12/create-jellyfin-12-bridge';
import { waitForJellyfin12Globals } from './jellyfin/jellyfin12/wait-for-jellyfin-12-globals';

const LOG_PREFIX = '[STRM Manager]';

async function bootstrap(): Promise<void> {
    try {
        const environment = inspectJellyfinEnvironment();

        console.info(
            `${LOG_PREFIX} Jellyfin Web bootstrap loaded.`,
            environment,
        );

        if (!environment.browser) {
            return;
        }

        const globals = await waitForJellyfin12Globals();
        const bridge = await createJellyfin12Bridge(globals);

        console.info(
            `${LOG_PREFIX} Jellyfin bridge ready.`,
            {
                version: bridge.version,
                capabilities: bridge.capabilities,
            },
        );
    } catch (error) {
        console.error(
            `${LOG_PREFIX} Jellyfin integration unavailable.`,
            error,
        );
    }
}

void bootstrap();