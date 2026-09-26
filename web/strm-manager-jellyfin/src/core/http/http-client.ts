export interface HttpClient {
    get<T>(path: string): Promise<T>;

    post<TResponse, TBody = unknown>(
        path: string,
        body?: TBody,
    ): Promise<TResponse>;
}