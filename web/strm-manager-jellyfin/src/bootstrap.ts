import { inspectJellyfinEnvironment } from './jellyfin/capabilities/inspect-jellyfin-environment';
import { createJellyfin12Bridge } from './jellyfin/jellyfin12/create-jellyfin-12-bridge';
import { startJellyfin12HomeIntegration } from './jellyfin/jellyfin12/start-jellyfin-12-home-integration';
import { waitForJellyfin12Globals } from './jellyfin/jellyfin12/wait-for-jellyfin-12-globals';
import { createJellyfin12RowComponents } from './jellyfin/jellyfin12/jellyfin-12-row-components';
import { createJellyfin12HttpClient } from './jellyfin/jellyfin12/jellyfin-12-http-client';
import { createHomeView } from './home/home-view';
import { createCatalogApi } from './catalog/catalog-api';
import { getAppConfig } from './core/config/app-config';
import { createAxiosHttpClient } from './core/http/axios-http-client';
import { createHomeController } from './home/home-controller';
import { createJellyfinLibraryApi } from './watch/jellyfin-library-api';
import { createMovieWatchFlow } from './watch/movie-watch-flow';
import { createMovieWatchApi } from './watch/movie-watch-api';
import { createHomeFeedback } from './home/home-feedback';
import { MovieWatchError } from './watch/movie-watch-error';
import './styles/strm-manager.css';

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

        const backendHttp = createAxiosHttpClient(
            config.strmManagerBaseUrl,
        );

        const jellyfinHttp = createJellyfin12HttpClient(
            globals,
        );

        const catalogApi = createCatalogApi(
            backendHttp,
        );

        const movieWatchApi = createMovieWatchApi(
            jellyfinHttp,
        );

        const jellyfinLibraryApi = createJellyfinLibraryApi(
            jellyfinHttp,
        );

        const movieWatchFlow =
            createMovieWatchFlow(
                movieWatchApi,
                jellyfinLibraryApi,
            );

        const nativeRowComponents = createJellyfin12RowComponents();

        const homeView =
            createHomeView(
                nativeRowComponents,
            );
        
        const homeFeedback =
            createHomeFeedback();

        const homeController =
            createHomeController(
                catalogApi,
                homeView,
                async (movie, interaction) => {
                    if (!movie.id) {
                        console.error(
                            `${LOG_PREFIX} Cannot watch movie without an external id.`,
                        );
                        return;
                    }

                    interaction.setPreparing(true);

                    try {
                        const itemId =
                            await movieWatchFlow.prepare(
                                movie.id,
                            );

                        await bridge.navigation.openItem({
                            id: itemId,
                        });
                    } catch (error) {
                        interaction.setPreparing(false);

                        if (
                            error instanceof
                            MovieWatchError
                        ) {
                            switch (error.code) {
                                case 'preparation-failed':
                                    homeFeedback.show(
                                        'Este filme não está disponível no momento.',
                                    );
                                    break;

                                case 'library-timeout':
                                    homeFeedback.show(
                                        'O filme foi preparado, mas demorou para aparecer no Jellyfin. Tente novamente.',
                                    );
                                    break;
                            }
                        } else {
                            homeFeedback.show(
                                'Não foi possível preparar o filme. Tente novamente em instantes.',
                            );
                        }

                        console.error(
                            `${LOG_PREFIX} Failed to prepare movie for playback.`,
                            error,
                        );
                    }
                },
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