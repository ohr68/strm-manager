export interface HttpClient {
    get<T>(path: string): Promise<T>;

    post<TResponse, TBody = unknown>(
        path: string,
        body?: TBody,
    ): Promise<TResponse>;
}

export class HttpError extends Error {
    constructor(
        readonly status: number,
        message?: string,
    ) {
        super(
            message ??
                `HTTP request failed with status ${status}.`,
        );

        this.name = 'HttpError';
    }
}
