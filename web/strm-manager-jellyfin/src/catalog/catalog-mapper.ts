import type {
    CatalogMovieDto,
    MovieRowsResponse,
} from './catalog-dtos';
import type { MediaCardModel } from '../home/components/media-card';
import type { MediaRowModel } from '../home/components/media-row';

export function mapMovieRowsToMediaRows(
    rows: MovieRowsResponse,
): readonly MediaRowModel[] {
    return rows.map((row) => ({
        id: row.id,
        title: row.name,
        items: row.movies.map(mapMovieToMediaCard),
    }));
}

function mapMovieToMediaCard(
    movie: CatalogMovieDto,
): MediaCardModel {
    return {
        id: movie.externalId,
        title: movie.title,
        subtitle:
            movie.year === null
                ? undefined
                : String(movie.year),
        imageUrl:
            movie.posterUrl ?? undefined,
    };
}
