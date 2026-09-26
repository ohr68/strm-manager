import type { HttpClient } from '../core/http/http-client';
import type { MovieRowsResponse } from './catalog-dtos';

export interface CatalogApi {
    getMovieRows(): Promise<MovieRowsResponse>;
}

export function createCatalogApi(
    http: HttpClient,
): CatalogApi {
    return {
        getMovieRows(): Promise<MovieRowsResponse> {
            return http.get<MovieRowsResponse>(
                '/api/catalog/movies/rows',
            );
        },
    };
}
