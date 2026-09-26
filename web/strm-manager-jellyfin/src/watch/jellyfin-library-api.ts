import {
    HttpError,
    type HttpClient,
} from '../core/http/http-client';

export interface JellyfinMovieResult {
    readonly itemId: string;
}

export interface JellyfinLibraryApi {
    findMovieByImdb(
        imdbId: string,
    ): Promise<JellyfinMovieResult | null>;

    scan(): Promise<void>;
}

export function createJellyfinLibraryApi(
    http: HttpClient,
): JellyfinLibraryApi {
    return {
        async findMovieByImdb(
            imdbId: string,
        ): Promise<JellyfinMovieResult | null> {
            try {
                return await http.get<JellyfinMovieResult>(
                    `/StrmManager/Libraries/Movies/By-Imdb/${encodeURIComponent(imdbId)}`,
                );
            } catch (error) {
                if (
                    error instanceof HttpError &&
                    error.status === 404
                ) {
                    return null;
                }

                throw error;
            }
        },

        async scan(): Promise<void> {
            await http.post<unknown>(
                '/StrmManager/Libraries/Scan',
            );
        },
    };
}
