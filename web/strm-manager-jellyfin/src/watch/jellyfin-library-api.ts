import type {
    HttpClient,
} from '../core/http/http-client';

export interface JellyfinMovieResult {
    readonly itemId: string;
}

interface JellyfinItemDto {
    readonly Id?: string;
}

interface JellyfinItemsResponse {
    readonly Items?: readonly JellyfinItemDto[];
}

export interface JellyfinLibraryApi {
    findMovieByImdb(
        imdbId: string,
    ): Promise<JellyfinMovieResult | null>;

    scan(): Promise<void>;
}

export function createJellyfinLibraryApi(
    http: HttpClient,
    userId: string,
): JellyfinLibraryApi {
    return {
        async findMovieByImdb(
            imdbId: string,
        ): Promise<JellyfinMovieResult | null> {
            const query = new URLSearchParams({
                Recursive: 'true',
                IncludeItemTypes: 'Movie',
                AnyProviderIdEquals: `imdb.${imdbId}`,
                Limit: '2',
            });

            const response =
                await http.get<JellyfinItemsResponse>(
                    `/Users/${encodeURIComponent(userId)}/Items?${query.toString()}`,
                );

            const items =
                response.Items ?? [];

            if (items.length === 0) {
                return null;
            }

            const itemId = items[0]?.Id;

            if (!itemId) {
                return null;
            }

            return {
                itemId,
            };
        },

        async scan(): Promise<void> {
            await http.post<unknown>(
                '/Library/Refresh',
            );
        },
    };
}
