import {
    describe,
    expect,
    it,
    vi,
} from 'vitest';

import type {
    CatalogApi,
} from '../../catalog/catalog-api';

import type {
    MediaCardInteraction,
    MediaCardModel,
} from '../components/media-card';

import type {
    HomeView,
} from '../home-view';

import {
    createHomeController,
} from '../home-controller';

describe('createHomeController', () => {
    it('renders loading before catalog rows are available', async () => {
        let resolveCatalog:
            ((value: Awaited<
                ReturnType<CatalogApi['getMovieRows']>
            >) => void) | undefined;

        const catalogPromise =
            new Promise<
                Awaited<
                    ReturnType<
                        CatalogApi['getMovieRows']
                    >
                >
            >((resolve) => {
                resolveCatalog = resolve;
            });

        const catalogApi: CatalogApi = {
            getMovieRows: vi
                .fn()
                .mockReturnValue(
                    catalogPromise,
                ),
        };

        const renderLoading = vi.fn();
        const render = vi.fn();

        const homeView: HomeView = {
            renderLoading,
            render,
        };

        const controller =
            createHomeController(
                catalogApi,
                homeView,
            );

        const root = {} as HTMLElement;

        const loadPromise =
            controller.load(root);

        expect(renderLoading)
            .toHaveBeenCalledOnce();

        expect(renderLoading)
            .toHaveBeenCalledWith(root);

        expect(catalogApi.getMovieRows)
            .toHaveBeenCalledOnce();

        expect(render)
            .not.toHaveBeenCalled();

        resolveCatalog?.({
            rows: [],
        });

        await loadPromise;

        expect(render)
            .toHaveBeenCalledOnce();
    });

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

        const renderLoading = vi.fn();
        const render = vi.fn();

        const homeView: HomeView = {
            renderLoading,
            render,
        };

        const controller =
            createHomeController(
                catalogApi,
                homeView,
            );

        const root = {} as HTMLElement;

        await controller.load(root);

        expect(renderLoading)
            .toHaveBeenCalledOnce();

        expect(renderLoading)
            .toHaveBeenCalledWith(root);

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

    it('forwards selected movies and interaction', async () => {
        const catalogApi: CatalogApi = {
            getMovieRows: vi.fn().mockResolvedValue({
                rows: [],
            }),
        };

        const renderLoading = vi.fn();
        const render = vi.fn();

        const homeView: HomeView = {
            renderLoading,
            render,
        };

        const onMovieSelected = vi.fn();

        const controller =
            createHomeController(
                catalogApi,
                homeView,
                onMovieSelected,
            );

        const root = {} as HTMLElement;

        await controller.load(root);

        const onSelect =
            render.mock.calls[0][2] as (
                movie: MediaCardModel,
                interaction: MediaCardInteraction,
            ) => void;

        const movie: MediaCardModel = {
            id: 'movie-1',
            title: 'Joker',
            subtitle: '2019',
            imageUrl: '/joker.jpg',
        };

        const interaction:
            MediaCardInteraction = {
                setPreparing: vi.fn(),
            };

        onSelect(
            movie,
            interaction,
        );

        expect(onMovieSelected)
            .toHaveBeenCalledOnce();

        expect(onMovieSelected)
            .toHaveBeenCalledWith(
                movie,
                interaction,
            );
    });
});
