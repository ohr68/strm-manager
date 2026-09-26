import type {
    MovieWatchApi,
} from './movie-watch-api';
import {
    MovieWatchError,
} from './movie-watch-error';
import type {
    JellyfinLibraryApi,
} from './jellyfin-library-api';

const STATUS_POLL_INTERVAL_MS = 1_500;
const LIBRARY_POLL_INTERVAL_MS = 1_500;
const LIBRARY_POLL_WINDOW_MS = 45_000;
const LIBRARY_STABLE_POLLS_REQUIRED = 5;
const LIBRARY_STABLE_MIN_MS = 6_000;

const PROCESSING_STATUSES =
    new Set([
        'Pending',
        'Searching',
        'Validating',
    ]);

export interface MovieWatchFlow {
    prepare(
        imdbId: string,
    ): Promise<string>;
}

export interface MovieWatchFlowClock {
    now(): number;
    delay(milliseconds: number): Promise<void>;
}

const defaultClock: MovieWatchFlowClock = {
    now: () => Date.now(),

    delay: (milliseconds) =>
        new Promise((resolve) => {
            window.setTimeout(
                resolve,
                milliseconds,
            );
        }),
};

export function createMovieWatchFlow(
    watchApi: MovieWatchApi,
    libraryApi: JellyfinLibraryApi,
    clock: MovieWatchFlowClock = defaultClock,
): MovieWatchFlow {
    return {
        async prepare(
            imdbId: string,
        ): Promise<string> {
            const watch =
                await watchApi.watch(imdbId);

            let status = watch.status;

            while (
                PROCESSING_STATUSES.has(status)
            ) {
                await clock.delay(
                    STATUS_POLL_INTERVAL_MS,
                );

                const movie =
                    await watchApi.getMovie(
                        imdbId,
                    );

                status = movie.status;
            }

            if (status !== 'Completed') {
                throw new MovieWatchError(
                    'preparation-failed',
                    `Movie preparation ended with status ${status}.`,
                );
            }

            const existing =
                await libraryApi.findMovieByImdb(
                    imdbId,
                );

            if (existing) {
                return existing.itemId;
            }

            await libraryApi.scan();

            return waitForStableJellyfinItem(
                imdbId,
                libraryApi,
                clock,
            );
        },
    };
}

async function waitForStableJellyfinItem(
    imdbId: string,
    libraryApi: JellyfinLibraryApi,
    clock: MovieWatchFlowClock,
): Promise<string> {
    const startedAt = clock.now();

    let stableCount = 0;
    let candidateSince: number | null = null;
    let candidateItemId: string | null = null;

    while (
        clock.now() - startedAt <
        LIBRARY_POLL_WINDOW_MS
    ) {
        const candidate =
            await libraryApi.findMovieByImdb(
                imdbId,
            );

        if (!candidate) {
            stableCount = 0;
            candidateSince = null;
            candidateItemId = null;
        } else {
            const now = clock.now();

            if (
                candidateItemId !==
                candidate.itemId
            ) {
                candidateItemId =
                    candidate.itemId;

                candidateSince = now;
                stableCount = 1;
            } else {
                stableCount += 1;
            }

            if (
                stableCount >=
                    LIBRARY_STABLE_POLLS_REQUIRED &&
                candidateSince !== null &&
                now - candidateSince >=
                    LIBRARY_STABLE_MIN_MS
            ) {
                return candidate.itemId;
            }
        }

        await clock.delay(
            LIBRARY_POLL_INTERVAL_MS,
        );
    }

    throw new MovieWatchError(
        'library-timeout',
        'Jellyfin movie did not become ready in time.',
    );
}
