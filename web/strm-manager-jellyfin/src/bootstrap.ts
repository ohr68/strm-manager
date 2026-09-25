import { inspectJellyfinEnvironment } from './jellyfin/capabilities/inspectJellyfinEnvironment';

const LOG_PREFIX = '[STRM Manager]';

function bootstrap(): void {
    try {
        const environment = inspectJellyfinEnvironment();

        if (!environment.browser) {
            return;
        }

        console.info(
            `${LOG_PREFIX} Jellyfin Web bootstrap loaded.`,
            environment,
        );
    } catch (error) {
        console.error(
            `${LOG_PREFIX} Bootstrap failed without interrupting Jellyfin.`,
            error,
        );
    }
}

bootstrap();