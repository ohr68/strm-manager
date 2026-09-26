import type { MediaCardModel } from './components/media-card';
import {
    createMediaRow,
    type MediaRowModel,
} from './components/media-row';

export interface HomeView {
    render(
        root: HTMLElement,
        rows: readonly MediaRowModel[],
        onSelect?: (item: MediaCardModel) => void,
    ): void;
}

export function createHomeView(): HomeView {
    return {
        render(root, rows, onSelect): void {
            root.replaceChildren(
                ...rows.map((row) =>
                    createMediaRow(row, onSelect),
                ),
            );
        }
    };
}
