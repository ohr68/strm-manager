import {
    describe,
    expect,
    it,
    vi,
} from 'vitest';

import type {
    HttpClient,
} from '../../core/http/http-client';

import {
    createJellyfinLibraryApi,
} from '../jellyfin-library-api';

const USER_ID = 'user-1';

describe('createJellyfinLibraryApi', () => {
    it('returns the Jellyfin movie when found', async () => {
        const get = vi.fn()
            .mockResolvedValue({
                Items: [
                    {
                        Id: 'jellyfin-item',
                    },
                ],
            });

        const http: HttpClient = {
            get,
            post: vi.fn(),
        };

        const api =
            createJellyfinLibraryApi(
                http,
                USER_ID,
            );

        await expect(
            api.findMovieByImdb('tt0137523'),
        ).resolves.toEqual({
            itemId: 'jellyfin-item',
        });

        expect(get).toHaveBeenCalledTimes(1);

        const path =
            get.mock.calls[0]?.[0] as string;

        const [pathname, queryString] =
            path.split('?');

        expect(pathname).toBe(
            '/Users/user-1/Items',
        );

        const query =
            new URLSearchParams(queryString);

        expect(query.get('Recursive'))
            .toBe('true');

        expect(query.get('IncludeItemTypes'))
            .toBe('Movie');

        expect(query.get('AnyProviderIdEquals'))
            .toBe('imdb.tt0137523');

        expect(query.get('Limit'))
            .toBe('2');
    });

    it('encodes the Jellyfin user id', async () => {
        const get = vi.fn()
            .mockResolvedValue({
                Items: [],
            });

        const http: HttpClient = {
            get,
            post: vi.fn(),
        };

        const api =
            createJellyfinLibraryApi(
                http,
                'user/test',
            );

        await api.findMovieByImdb(
            'tt0137523',
        );

        expect(
            get.mock.calls[0]?.[0],
        ).toContain(
            '/Users/user%2Ftest/Items?',
        );
    });

    it('returns null when the Jellyfin movie is not ready', async () => {
        const http: HttpClient = {
            get: vi.fn()
                .mockResolvedValue({
                    Items: [],
                }),
            post: vi.fn(),
        };

        const api =
            createJellyfinLibraryApi(
                http,
                USER_ID,
            );

        await expect(
            api.findMovieByImdb('tt0137523'),
        ).resolves.toBeNull();
    });

    it('returns null when the Jellyfin item has no id', async () => {
        const http: HttpClient = {
            get: vi.fn()
                .mockResolvedValue({
                    Items: [
                        {},
                    ],
                }),
            post: vi.fn(),
        };

        const api =
            createJellyfinLibraryApi(
                http,
                USER_ID,
            );

        await expect(
            api.findMovieByImdb('tt0137523'),
        ).resolves.toBeNull();
    });

    it('propagates HTTP errors', async () => {
        const error =
            new Error('request failed');

        const http: HttpClient = {
            get: vi.fn()
                .mockRejectedValue(error),
            post: vi.fn(),
        };

        const api =
            createJellyfinLibraryApi(
                http,
                USER_ID,
            );

        await expect(
            api.findMovieByImdb('tt0137523'),
        ).rejects.toBe(error);
    });

    it('requests a native Jellyfin library scan', async () => {
        const post = vi.fn()
            .mockResolvedValue(undefined);

        const http: HttpClient = {
            get: vi.fn(),
            post,
        };

        const api =
            createJellyfinLibraryApi(
                http,
                USER_ID,
            );

        await api.scan();

        expect(post).toHaveBeenCalledWith(
            '/Library/Refresh',
        );
    });
});
