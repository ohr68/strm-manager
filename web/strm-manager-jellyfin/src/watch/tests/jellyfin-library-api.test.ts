import {
    describe,
    expect,
    it,
    vi,
} from 'vitest';

import {
    HttpError,
    type HttpClient,
} from '../../core/http/http-client';

import {
    createJellyfinLibraryApi,
} from '../jellyfin-library-api';

describe('createJellyfinLibraryApi', () => {
    it('returns the Jellyfin movie when found', async () => {
        const result = {
            itemId: 'jellyfin-item',
        };

        const get = vi.fn()
            .mockResolvedValue(result);

        const http: HttpClient = {
            get,
            post: vi.fn(),
        };

        const api =
            createJellyfinLibraryApi(http);

        await expect(
            api.findMovieByImdb('tt0137523'),
        ).resolves.toEqual(result);

        expect(get).toHaveBeenCalledWith(
            '/StrmManager/Libraries/Movies/By-Imdb/tt0137523',
        );
    });

    it('returns null when the Jellyfin movie is not ready', async () => {
        const get = vi.fn()
            .mockRejectedValue(
                new HttpError(404),
            );

        const http: HttpClient = {
            get,
            post: vi.fn(),
        };

        const api =
            createJellyfinLibraryApi(http);

        await expect(
            api.findMovieByImdb('tt0137523'),
        ).resolves.toBeNull();
    });

    it('propagates non-404 HTTP errors', async () => {
        const error =
            new HttpError(500);

        const get = vi.fn()
            .mockRejectedValue(error);

        const http: HttpClient = {
            get,
            post: vi.fn(),
        };

        const api =
            createJellyfinLibraryApi(http);

        await expect(
            api.findMovieByImdb('tt0137523'),
        ).rejects.toBe(error);
    });

    it('requests a library scan', async () => {
        const post = vi.fn()
            .mockResolvedValue(undefined);

        const http: HttpClient = {
            get: vi.fn(),
            post,
        };

        const api =
            createJellyfinLibraryApi(http);

        await api.scan();

        expect(post).toHaveBeenCalledWith(
            '/StrmManager/Libraries/Scan',
        );
    });
});
