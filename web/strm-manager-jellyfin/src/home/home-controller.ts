import type { CatalogApi } from '../catalog/catalog-api';
import { mapMovieRowsToMediaRows } from '../catalog/catalog-mapper';
import type { HomeView } from './home-view';

export interface HomeController {
    load(root: HTMLElement): Promise<void>;
}

export function createHomeController(
    catalogApi: CatalogApi,
    homeView: HomeView,
): HomeController {
    return {
        async load(root: HTMLElement): Promise<void> {
            const rows = await catalogApi.getMovieRows();

            homeView.render(
                root,
                mapMovieRowsToMediaRows(rows),
            );
        },
    };
}
