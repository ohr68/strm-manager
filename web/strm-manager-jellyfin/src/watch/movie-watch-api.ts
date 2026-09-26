import type {
    HttpClient,
} from '../core/http/http-client';

export interface MovieWatchResult {
    readonly movieId: string;
    readonly imdbId: string;
    readonly status: string;
}

export interface MovieStatusResult {
    readonly movieId: string;
    readonly imdbId: string;
    readonly title: string;
    readonly year: number | null;
    readonly status: string;
}

export interface MovieWatchApi {
    watch(
        imdbId: string,
    ): Promise<MovieWatchResult>;

    getMovie(
        imdbId: string,
    ): Promise<MovieStatusResult>;
}

export function createMovieWatchApi(
    http: HttpClient,
): MovieWatchApi {
    return {
        watch(
            imdbId: string,
        ): Promise<MovieWatchResult> {
            return http.post<MovieWatchResult>(
                `/StrmManager/Movies/Watch/${encodeURIComponent(imdbId)}`,
            );
        },

        getMovie(
            imdbId: string,
        ): Promise<MovieStatusResult> {
            return http.get<MovieStatusResult>(
                `/StrmManager/Movies/By-Imdb/${encodeURIComponent(imdbId)}`,
            );
        },
    };
}
