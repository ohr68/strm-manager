import {
    describe,
    expect,
    it,
    vi,
} from 'vitest';

import {
    HttpError,
} from '../../../core/http/http-client';

import type {
    Jellyfin12AjaxOptions,
    Jellyfin12Globals,
} from '../jellyfin-12-globals';

import {
    createJellyfin12HttpClient,
} from '../jellyfin-12-http-client';

function createGlobals(
    ajax: (
        options: Jellyfin12AjaxOptions,
    ) => Promise<unknown>,
): Jellyfin12Globals {
    return {
        ApiClient: {
            getCurrentUserId: () => 'user-1',
            getSystemInfo: async () => ({
                Version: '12.1.0',
            }),
            serverInfo: () => ({
                Id: 'server-1',
            }),
            getUrl: (path) =>
                `http://jellyfin/${path}`,
            async ajax<T>(
                options: Jellyfin12AjaxOptions,
            ): Promise<T> {
                return await ajax(
                    options,
                ) as T;
            },
        },
    };
}

describe('createJellyfin12HttpClient', () => {
    it('performs an authenticated GET', async () => {
        const ajax = vi.fn()
            .mockResolvedValue({
                itemId: 'item-1',
            });

        const http =
            createJellyfin12HttpClient(
                createGlobals(ajax),
            );

        await expect(
            http.get('/StrmManager/test'),
        ).resolves.toEqual({
            itemId: 'item-1',
        });

        expect(ajax).toHaveBeenCalledWith({
            type: 'GET',
            url:
                'http://jellyfin/StrmManager/test',
            dataType: 'json',
        });
    });

    it('performs an authenticated POST', async () => {
        const ajax = vi.fn()
            .mockResolvedValue({
                status: 'Pending',
            });

        const http =
            createJellyfin12HttpClient(
                createGlobals(ajax),
            );

        await expect(
            http.post('/StrmManager/test'),
        ).resolves.toEqual({
            status: 'Pending',
        });

        expect(ajax).toHaveBeenCalledWith({
            type: 'POST',
            url:
                'http://jellyfin/StrmManager/test',
            dataType: 'json',
        });
    });

    it('normalizes Jellyfin HTTP errors', async () => {
        const ajax = vi.fn()
            .mockRejectedValue({
                status: 404,
            });

        const http =
            createJellyfin12HttpClient(
                createGlobals(ajax),
            );

        try {
            await http.get(
                '/StrmManager/test',
            );

            throw new Error(
                'Expected request to fail.',
            );
        } catch (error) {
            expect(error).toBeInstanceOf(
                HttpError,
            );

            expect(
                (error as HttpError).status,
            ).toBe(404);
        }
    });

    it('fails when authenticated transport is unavailable', () => {
        const globals: Jellyfin12Globals = {
            ApiClient: {
                getCurrentUserId: () =>
                    'user-1',
                getSystemInfo: async () => ({
                    Version: '12.1.0',
                }),
                serverInfo: () => ({
                    Id: 'server-1',
                }),
            },
        };

        expect(() =>
            createJellyfin12HttpClient(
                globals,
            ),
        ).toThrow(
            'Jellyfin authenticated HTTP transport is not available.',
        );
    });
});
