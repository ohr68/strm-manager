import {
    HttpError,
    type HttpClient,
} from '../../core/http/http-client';

import {
    getJellyfin12Globals,
    type Jellyfin12ApiClient,
    type Jellyfin12Globals,
} from './jellyfin-12-globals';

interface Jellyfin12RequestError {
    readonly status?: number;
}

function toHttpError(
    error: unknown,
): never {
    if (
        typeof error === 'object' &&
        error !== null &&
        'status' in error &&
        typeof (
            error as Jellyfin12RequestError
        ).status === 'number'
    ) {
        throw new HttpError(
            (
                error as Jellyfin12RequestError
            ).status!,
        );
    }

    throw error;
}

function requireTransport(
    globals: Jellyfin12Globals,
): {
    apiClient: Jellyfin12ApiClient;
    getUrl: (path: string) => string;
    ajax: <T>(
        options: {
            type: 'GET' | 'POST';
            url: string;
            dataType: 'json';
            data?: unknown;
        },
    ) => Promise<T>;
} {
    const apiClient = globals.ApiClient;

    if (!apiClient) {
        throw new Error(
            'Jellyfin ApiClient is not available.',
        );
    }

    if (
        typeof apiClient.getUrl !== 'function' ||
        typeof apiClient.ajax !== 'function'
    ) {
        throw new Error(
            'Jellyfin authenticated HTTP transport is not available.',
        );
    }

    return {
        apiClient,
        getUrl: apiClient.getUrl.bind(apiClient),
        ajax: apiClient.ajax.bind(apiClient),
    };
}

export function createJellyfin12HttpClient(
    globals: Jellyfin12Globals =
        getJellyfin12Globals(),
): HttpClient {
    const {
        getUrl,
        ajax,
    } = requireTransport(globals);

    return {
        async get<T>(
            path: string,
        ): Promise<T> {
            try {
                return await ajax<T>({
                    type: 'GET',
                    url: getUrl(
                        path.replace(/^\/+/, ''),
                    ),
                    dataType: 'json',
                });
            } catch (error) {
                return toHttpError(error);
            }
        },

        async post<
            TResponse,
            TBody = unknown,
        >(
            path: string,
            body?: TBody,
        ): Promise<TResponse> {
            try {
                return await ajax<TResponse>({
                    type: 'POST',
                    url: getUrl(
                        path.replace(/^\/+/, ''),
                    ),
                    dataType: 'json',
                    ...(body === undefined
                        ? {}
                        : { data: body }),
                });
            } catch (error) {
                return toHttpError(error);
            }
        },
    };
}
