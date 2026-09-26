import {
    describe,
    expect,
    it,
    vi,
} from 'vitest';
import type { CatalogApi } from '../../catalog/catalog-api';
import type { HomeView } from '../home-view';
import { createHomeController } from '../home-controller';
import type { MediaCardModel } from '../components/media-card';

describe('createHomeController', () => {
    it('loads catalog rows and renders the home view', async () => {
        const catalogApi: CatalogApi = {
            getMovieRows: vi.fn().mockResolvedValue({
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
            }),
        };

        const render = vi.fn();

        const homeView: HomeView = {
            render,
        };

        const controller = createHomeController(
            catalogApi,
            homeView,
        );

        const root = {} as HTMLElement;

        await controller.load(root);

        expect(catalogApi.getMovieRows)
            .toHaveBeenCalledOnce();

        expect(render)
            .toHaveBeenCalledOnce();

        expect(render)
            .toHaveBeenCalledWith(
                root,
                [
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
                ],
                expect.any(Function),
            );
    });

    it('forwards selected movies to the selection handler', async () => {
        const catalogApi: CatalogApi = {
            getMovieRows: vi.fn().mockResolvedValue({
                rows: [],
            }),
        };

        const render = vi.fn();

        const homeView: HomeView = {
            render,
        };

        const onMovieSelected = vi.fn();

        const controller = createHomeController(
            catalogApi,
            homeView,
            onMovieSelected,
        );

        const root = {} as HTMLElement;

        await controller.load(root);

        const onSelect = render.mock.calls[0][2] as (
            movie: MediaCardModel,
        ) => void;

        const movie: MediaCardModel = {
            id: 'movie-1',
            title: 'Joker',
            subtitle: '2019',
            imageUrl: '/joker.jpg',
        };

        onSelect(movie);

        expect(onMovieSelected)
            .toHaveBeenCalledOnce();

        expect(onMovieSelected)
            .toHaveBeenCalledWith(movie);
    });
});
