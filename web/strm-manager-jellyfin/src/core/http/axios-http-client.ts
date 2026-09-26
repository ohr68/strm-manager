import axios, {
    type AxiosInstance,
} from 'axios';
import {
    HttpError,
    type HttpClient,
} from './http-client';

function toHttpError(
    error: unknown,
): never {
    if (
        axios.isAxiosError(error) &&
        error.response
    ) {
        throw new HttpError(
            error.response.status,
        );
    }

    throw error;
}

export function createAxiosHttpClient(
    baseUrl: string,
): HttpClient {
    const client: AxiosInstance = axios.create({
        baseURL: baseUrl.replace(/\/+$/, ''),
        timeout: 15_000,
        headers: {
            Accept: 'application/json',
        },
    });

    return {
        async get<T>(path: string): Promise<T> {
            try {
                const response =
                    await client.get<T>(path);

                return response.data;
            } catch (error) {
                return toHttpError(error);
            }
        },

        async post<TResponse, TBody = unknown>(
            path: string,
            body?: TBody,
        ): Promise<TResponse> {
            try {
                const response =
                    await client.post<TResponse>(
                        path,
                        body,
                    );

                return response.data;
            } catch (error) {
                return toHttpError(error);
            }
        },
    };
}
