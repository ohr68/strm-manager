import {
    describe,
    expect,
    it,
} from 'vitest';
import { mapMovieRowsToMediaRows } from '../catalog-mapper';

describe('mapMovieRowsToMediaRows', () => {
    it('maps catalog rows to home media rows', () => {
        const result = mapMovieRowsToMediaRows({
            rows: [
                {
                    id: 'popular',
                    name: 'Popular',
                    movies: [
                        {
                            externalId: 'movie-1',
                            title: 'Joker',
                            year: 2019,
                            posterUrl: '/joker.jpg',
                        },
                    ],
                },
            ],
        });

        expect(result).toEqual([
            {
                id: 'popular',
                title: 'Popular',
                items: [
                    {
                        id: 'movie-1',
                        title: 'Joker',
                        subtitle: '2019',
                        imageUrl: '/joker.jpg',
                    },
                ],
            },
        ]);
    });

    it('omits optional presentation values when absent', () => {
        const result = mapMovieRowsToMediaRows({
            rows: [
                {
                    id: 'popular',
                    name: 'Popular',
                    movies: [
                        {
                            externalId: 'movie-1',
                            title: 'Unknown',
                            year: null,
                            posterUrl: null,
                        },
                    ],
                },
            ],
        });

        expect(result[0]?.items[0]).toEqual({
            id: 'movie-1',
            title: 'Unknown',
            subtitle: undefined,
            imageUrl: undefined,
        });
    });
});
