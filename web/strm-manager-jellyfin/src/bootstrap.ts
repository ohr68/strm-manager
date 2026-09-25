import { inspectJellyfinEnvironment } from './jellyfin/capabilities/inspect-jellyfin-environment';
import { createJellyfin12Bridge } from './jellyfin/jellyfin12/create-jellyfin-12-bridge';
import { startJellyfin12HomeIntegration } from './jellyfin/jellyfin12/start-jellyfin-12-home-integration';
import { waitForJellyfin12Globals } from './jellyfin/jellyfin12/wait-for-jellyfin-12-globals';
import { renderHomeRow } from './home/render-home-row';

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

        const homeIntegration =
        startJellyfin12HomeIntegration(
            (root) => {
                renderHomeRow(
                    root,
                    {
                        title: 'STRM Manager',
                        items: [
                            {
                                title: 'Joker',
                                subtitle: '2019',
                            },
                        ],
                    },
                );
            },
        );

        if (!homeIntegration) {
            console.info(
                `${LOG_PREFIX} Jellyfin home integration unavailable.`,
            );

            return;
        }

        console.info(
            `${LOG_PREFIX} Jellyfin home integration started.`,
        );
    } catch (error) {
        console.error(
            `${LOG_PREFIX} Jellyfin integration unavailable.`,
            error,
        );
    }
}

void bootstrap();