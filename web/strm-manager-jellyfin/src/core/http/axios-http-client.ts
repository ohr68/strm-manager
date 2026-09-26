import axios, {
    type AxiosInstance,
} from 'axios';
import type { HttpClient } from './http-client';

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
            const response = await client.get<T>(path);

            return response.data;
        },

        async post<TResponse, TBody = unknown>(
            path: string,
            body?: TBody,
        ): Promise<TResponse> {
            const response = await client.post<TResponse>(
                path,
                body,
            );

            return response.data;
        },
    };
}