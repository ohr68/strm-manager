export interface CatalogMovieDto {
    readonly externalId: string;
    readonly title: string;
    readonly year: number | null;
    readonly posterUrl: string | null;
}

export interface MovieRowDto {
    readonly id: string;
    readonly name: string;
    readonly movies: readonly CatalogMovieDto[];
}

export interface MovieRowsResponse {
    readonly rows: readonly MovieRowDto[];
}
