import {
    describe,
    expect,
    it,
    vi,
} from 'vitest';
import type { HttpClient } from '../../core/http/http-client';
import { createCatalogApi } from '../catalog-api';

describe('createCatalogApi', () => {
    it('requests movie rows from the catalog endpoint', async () => {
        const response = {
            rows: [],
        };

        const get = vi.fn().mockResolvedValue(response);

        const http: HttpClient = {
            get,
            post: vi.fn(),
        };

        const api = createCatalogApi(http);

        await expect(
            api.getMovieRows(),
        ).resolves.toEqual(response);

        expect(get).toHaveBeenCalledOnce();
        expect(get).toHaveBeenCalledWith(
            '/api/catalog/movies/rows',
        );
    });
});
