import {
    describe,
    expect,
    it,
    vi,
} from 'vitest';

import type {
    JellyfinLibraryApi,
} from '../jellyfin-library-api';

import {
    createMovieWatchFlow,
    type MovieWatchFlowClock,
} from '../movie-watch-flow';

import {
    MovieWatchError,
} from '../movie-watch-error';

import type {
    MovieWatchApi,
} from '../movie-watch-api';

const IMDB_ID = 'tt0137523';
const MOVIE_ID = 'movie-id';
const JELLYFIN_ITEM_ID = 'jellyfin-item';

function createWatchApi(
    overrides: Partial<MovieWatchApi> = {},
): MovieWatchApi {
    return {
        watch: vi.fn().mockResolvedValue({
            movieId: MOVIE_ID,
            imdbId: IMDB_ID,
            status: 'Completed',
        }),

        getMovie: vi.fn().mockResolvedValue({
            movieId: MOVIE_ID,
            imdbId: IMDB_ID,
            title: 'Fight Club',
            year: 1999,
            status: 'Completed',
        }),

        ...overrides,
    };
}

function createLibraryApi(
    overrides: Partial<JellyfinLibraryApi> = {},
): JellyfinLibraryApi {
    return {
        findMovieByImdb:
            vi.fn().mockResolvedValue({
                itemId: JELLYFIN_ITEM_ID,
            }),

        scan: vi.fn().mockResolvedValue(
            undefined,
        ),

        ...overrides,
    };
}

function createAdvancingClock(): {
    clock: MovieWatchFlowClock;
    getNow(): number;
} {
    let now = 0;

    const clock: MovieWatchFlowClock = {
        now: () => now,

        delay: vi.fn(
            async (
                milliseconds: number,
            ): Promise<void> => {
                now += milliseconds;
            },
        ),
    };

    return {
        clock,
        getNow: () => now,
    };
}

describe('MovieWatchFlow', () => {
    it(
        'exposes typed movie watch errors',
        () => {
            const error =
                new MovieWatchError(
                    'preparation-failed',
                    'Preparation failed.',
                );

            expect(error)
                .toBeInstanceOf(Error);

            expect(error.name)
                .toBe('MovieWatchError');

            expect(error.code)
                .toBe(
                    'preparation-failed',
                );

            expect(error.message)
                .toBe(
                    'Preparation failed.',
                );
        },
    );

    it(
        'returns an existing Jellyfin item without scanning',
        async () => {
            const watchApi =
                createWatchApi();

            const libraryApi =
                createLibraryApi();

            const flow =
                createMovieWatchFlow(
                    watchApi,
                    libraryApi,
                );

            await expect(
                flow.prepare(IMDB_ID),
            ).resolves.toBe(
                JELLYFIN_ITEM_ID,
            );

            expect(
                watchApi.watch,
            ).toHaveBeenCalledOnce();

            expect(
                watchApi.watch,
            ).toHaveBeenCalledWith(
                IMDB_ID,
            );

            expect(
                watchApi.getMovie,
            ).not.toHaveBeenCalled();

            expect(
                libraryApi
                    .findMovieByImdb,
            ).toHaveBeenCalledOnce();

            expect(
                libraryApi.scan,
            ).not.toHaveBeenCalled();
        },
    );

    it(
        'polls processing status until Completed',
        async () => {
            const watchApi =
                createWatchApi({
                    watch: vi.fn()
                        .mockResolvedValue({
                            movieId:
                                MOVIE_ID,
                            imdbId:
                                IMDB_ID,
                            status:
                                'Pending',
                        }),

                    getMovie: vi.fn()
                        .mockResolvedValueOnce({
                            movieId:
                                MOVIE_ID,
                            imdbId:
                                IMDB_ID,
                            title:
                                'Fight Club',
                            year: 1999,
                            status:
                                'Searching',
                        })
                        .mockResolvedValueOnce({
                            movieId:
                                MOVIE_ID,
                            imdbId:
                                IMDB_ID,
                            title:
                                'Fight Club',
                            year: 1999,
                            status:
                                'Completed',
                        }),
                });

            const libraryApi =
                createLibraryApi();

            const clock:
                MovieWatchFlowClock = {
                    now: () => 0,

                    delay: vi.fn()
                        .mockResolvedValue(
                            undefined,
                        ),
                };

            const flow =
                createMovieWatchFlow(
                    watchApi,
                    libraryApi,
                    clock,
                );

            await expect(
                flow.prepare(IMDB_ID),
            ).resolves.toBe(
                JELLYFIN_ITEM_ID,
            );

            expect(
                watchApi.getMovie,
            ).toHaveBeenCalledTimes(2);

            expect(
                watchApi.getMovie,
            ).toHaveBeenNthCalledWith(
                1,
                IMDB_ID,
            );

            expect(
                watchApi.getMovie,
            ).toHaveBeenNthCalledWith(
                2,
                IMDB_ID,
            );

            expect(
                clock.delay,
            ).toHaveBeenCalledTimes(2);

            expect(
                clock.delay,
            ).toHaveBeenNthCalledWith(
                1,
                1_500,
            );

            expect(
                clock.delay,
            ).toHaveBeenNthCalledWith(
                2,
                1_500,
            );

            expect(
                libraryApi.scan,
            ).not.toHaveBeenCalled();
        },
    );

    it(
        'scans once and waits for a stable Jellyfin item',
        async () => {
            const watchApi =
                createWatchApi();

            const findMovieByImdb =
                vi.fn()
                    // Completed fast path:
                    .mockResolvedValueOnce(
                        null,
                    )
                    // Readiness polls:
                    .mockResolvedValue({
                        itemId:
                            JELLYFIN_ITEM_ID,
                    });

            const libraryApi =
                createLibraryApi({
                    findMovieByImdb,
                });

            const {
                clock,
                getNow,
            } = createAdvancingClock();

            const flow =
                createMovieWatchFlow(
                    watchApi,
                    libraryApi,
                    clock,
                );

            await expect(
                flow.prepare(IMDB_ID),
            ).resolves.toBe(
                JELLYFIN_ITEM_ID,
            );

            expect(
                libraryApi.scan,
            ).toHaveBeenCalledOnce();

            /*
             * One lookup is the Completed fast path,
             * followed by five stable observations:
             *
             * t=0
             * t=1500
             * t=3000
             * t=4500
             * t=6000
             */
            expect(
                findMovieByImdb,
            ).toHaveBeenCalledTimes(6);

            expect(
                clock.delay,
            ).toHaveBeenCalledTimes(4);

            expect(
                getNow(),
            ).toBe(6_000);
        },
    );

    it(
        'resets readiness stability after a missing item',
        async () => {
            const watchApi =
                createWatchApi();

            const findMovieByImdb =
                vi.fn()
                    // Completed fast path.
                    .mockResolvedValueOnce(
                        null,
                    )

                    // First candidate sequence.
                    .mockResolvedValueOnce({
                        itemId:
                            JELLYFIN_ITEM_ID,
                    })
                    .mockResolvedValueOnce({
                        itemId:
                            JELLYFIN_ITEM_ID,
                    })

                    // Equivalent to the old
                    // readiness 404.
                    .mockResolvedValueOnce(
                        null,
                    )

                    // Candidate must start
                    // stabilizing again.
                    .mockResolvedValue({
                        itemId:
                            JELLYFIN_ITEM_ID,
                    });

            const libraryApi =
                createLibraryApi({
                    findMovieByImdb,
                });

            const {
                clock,
                getNow,
            } = createAdvancingClock();

            const flow =
                createMovieWatchFlow(
                    watchApi,
                    libraryApi,
                    clock,
                );

            await expect(
                flow.prepare(IMDB_ID),
            ).resolves.toBe(
                JELLYFIN_ITEM_ID,
            );

            expect(
                libraryApi.scan,
            ).toHaveBeenCalledOnce();

            /*
             * Timeline after scan:
             *
             * 0     item
             * 1500  item
             * 3000  null  -> reset
             * 4500  item   -> new candidate
             * 6000  item
             * 7500  item
             * 9000  item
             * 10500 item   -> 5 polls + 6000 ms
             */
            expect(
                getNow(),
            ).toBe(10_500);

            expect(
                findMovieByImdb,
            ).toHaveBeenCalledTimes(9);
        },
    );

    it(
        'treats a non-processing non-Completed status as terminal',
        async () => {
            const watchApi =
                createWatchApi({
                    watch: vi.fn()
                        .mockResolvedValue({
                            movieId:
                                MOVIE_ID,
                            imdbId:
                                IMDB_ID,
                            status:
                                'Unavailable',
                        }),
                });

            const libraryApi =
                createLibraryApi();

            const flow =
                createMovieWatchFlow(
                    watchApi,
                    libraryApi,
                );

            await expect(
                flow.prepare(IMDB_ID),
            ).rejects.toMatchObject({
                name:
                    'MovieWatchError',
                code:
                    'preparation-failed',
                message:
                    'Movie preparation ended with status Unavailable.',
            });

            expect(
                watchApi.getMovie,
            ).not.toHaveBeenCalled();

            expect(
                libraryApi
                    .findMovieByImdb,
            ).not.toHaveBeenCalled();

            expect(
                libraryApi.scan,
            ).not.toHaveBeenCalled();
        },
    );

    it(
        'times out when the Jellyfin item never becomes ready',
        async () => {
            const watchApi =
                createWatchApi();

            const libraryApi =
                createLibraryApi({
                    findMovieByImdb:
                        vi.fn()
                            .mockResolvedValue(
                                null,
                            ),
                });

            const {
                clock,
                getNow,
            } = createAdvancingClock();

            const flow =
                createMovieWatchFlow(
                    watchApi,
                    libraryApi,
                    clock,
                );

            await expect(
                flow.prepare(IMDB_ID),
            ).rejects.toMatchObject({
                name:
                    'MovieWatchError',
                code:
                    'library-timeout',
                message:
                    'Jellyfin movie did not become ready in time.',
            });

            expect(
                libraryApi.scan,
            ).toHaveBeenCalledOnce();

            expect(
                getNow(),
            ).toBe(45_000);
        },
    );
});
