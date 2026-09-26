import { inspectJellyfinEnvironment } from './jellyfin/capabilities/inspect-jellyfin-environment';
import { createJellyfin12Bridge } from './jellyfin/jellyfin12/create-jellyfin-12-bridge';
import { startJellyfin12HomeIntegration } from './jellyfin/jellyfin12/start-jellyfin-12-home-integration';
import { waitForJellyfin12Globals } from './jellyfin/jellyfin12/wait-for-jellyfin-12-globals';
import { createJellyfin12RowComponents } from './jellyfin/jellyfin12/jellyfin-12-row-components';
import { createHomeView } from './home/home-view';
import { createCatalogApi } from './catalog/catalog-api';
import { getAppConfig } from './core/config/app-config';
import { createAxiosHttpClient } from './core/http/axios-http-client';
import { createHomeController } from './home/home-controller';

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

        const config = getAppConfig();

        if (!config) {
            console.info(
                `${LOG_PREFIX} STRM Manager API configuration unavailable.`,
            );

            return;
        }

        const http = createAxiosHttpClient(
            config.strmManagerBaseUrl,
        );

        const catalogApi = createCatalogApi(http);
        const nativeRowComponents = createJellyfin12RowComponents();
        const homeView = createHomeView(nativeRowComponents);

        const homeController = createHomeController(
            catalogApi,
            homeView,
        );

        const homeIntegration =
        startJellyfin12HomeIntegration(
            (root) => {
                void homeController
                    .load(root)
                    .catch((error: unknown) => {
                        console.error(
                            `${LOG_PREFIX} Failed to load home catalog.`,
                            error,
                        );
                    });
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