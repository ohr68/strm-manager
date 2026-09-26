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
    createMovieWatchApi,
} from '../movie-watch-api';

describe('createMovieWatchApi', () => {
    it('starts watch intent for the IMDb id', async () => {
        const result = {
            movieId:
                '1393e862-e5f1-4fe6-b4a0-b3bb8ee0daa6',
            imdbId: 'tt0137523',
            status: 'Pending',
        };

        const post = vi.fn()
            .mockResolvedValue(result);

        const http: HttpClient = {
            get: vi.fn(),
            post,
        };

        const api =
            createMovieWatchApi(http);

        await expect(
            api.watch('tt0137523'),
        ).resolves.toEqual(result);

        expect(post).toHaveBeenCalledWith(
            '/StrmManager/Movies/Watch/tt0137523',
        );
    });

    it('encodes the IMDb id in the request path', async () => {
        const post = vi.fn()
            .mockResolvedValue({
                movieId: 'movie-id',
                imdbId: 'tt/test',
                status: 'Pending',
            });

        const http: HttpClient = {
            get: vi.fn(),
            post,
        };

        const api =
            createMovieWatchApi(http);

        await api.watch('tt/test');

        expect(post).toHaveBeenCalledWith(
            '/StrmManager/Movies/Watch/tt%2Ftest',
        );
    });

    it('gets the current movie status by IMDb id', async () => {
        const result = {
            movieId: 'movie-id',
            imdbId: 'tt0137523',
            title: 'Fight Club',
            year: 1999,
            status: 'Searching',
        };

        const get = vi.fn()
            .mockResolvedValue(result);

        const http: HttpClient = {
            get,
            post: vi.fn(),
        };

        const api =
            createMovieWatchApi(http);

        await expect(
            api.getMovie('tt0137523'),
        ).resolves.toEqual(result);

        expect(get).toHaveBeenCalledWith(
            '/StrmManager/Movies/By-Imdb/tt0137523',
        );
    });
});
