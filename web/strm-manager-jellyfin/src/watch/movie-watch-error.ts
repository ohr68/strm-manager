export type MovieWatchErrorCode =
    | 'preparation-failed'
    | 'library-timeout';

export class MovieWatchError extends Error {
    constructor(
        readonly code: MovieWatchErrorCode,
        message: string,
        options?: ErrorOptions,
    ) {
        super(
            message,
            options,
        );

        this.name = 'MovieWatchError';
    }
}
