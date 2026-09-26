import type {
    CatalogApi,
} from '../catalog/catalog-api';

import {
    mapMovieRowsToMediaRows,
} from '../catalog/catalog-mapper';

import type {
    MediaCardModel,
} from './components/media-card';

import type {
    HomeView,
} from './home-view';

export type MovieSelectionHandler = (
    movie: MediaCardModel,
) => void | Promise<void>;

export interface HomeController {
    load(root: HTMLElement): Promise<void>;
}

export function createHomeController(
    catalogApi: CatalogApi,
    homeView: HomeView,
    onMovieSelected?: MovieSelectionHandler,
): HomeController {
    return {
        async load(
            root: HTMLElement,
        ): Promise<void> {
            const response =
                await catalogApi.getMovieRows();

            const rows =
                mapMovieRowsToMediaRows(
                    response,
                );

            homeView.render(
                root,
                rows,
                movie => {
                    void onMovieSelected?.(
                        movie,
                    );
                },
            );
        },
    };
}
