import {
    createMediaRow,
    type MediaRowModel,
} from './components/media-row';

export interface HomeView {
    render(
        root: HTMLElement,
        rows: readonly MediaRowModel[],
    ): void;
}

export function createHomeView(): HomeView {
    return {
        render(
            root: HTMLElement,
            rows: readonly MediaRowModel[],
        ): void {
            root.replaceChildren(
                ...rows.map(createMediaRow),
            );
        },
    };
}
