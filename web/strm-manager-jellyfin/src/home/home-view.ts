import type {
    MediaCardInteraction,
    MediaCardModel,
} from './components/media-card';

import {
    createMediaRow,
    type MediaRowModel,
} from './components/media-row';

import {
    createMediaRowSkeleton,
} from './components/media-row-skeleton';

import type {
    NativeRowComponents,
} from './native-row-components';

const SKELETON_ROW_COUNT = 2;

export interface HomeView {
    renderLoading(
        root: HTMLElement,
    ): void;

    render(
        root: HTMLElement,
        rows: readonly MediaRowModel[],
        onSelect?: (
            item: MediaCardModel,
            interaction: MediaCardInteraction,
        ) => void,
    ): void;
}

export function createHomeView(
    nativeComponents: NativeRowComponents,
): HomeView {
    return {
        renderLoading(
            root: HTMLElement,
        ): void {
            root.replaceChildren(
                ...Array.from(
                    {
                        length:
                            SKELETON_ROW_COUNT,
                    },
                    () =>
                        createMediaRowSkeleton(),
                ),
            );
        },

        render(
            root,
            rows,
            onSelect,
        ): void {
            root.replaceChildren(
                ...rows.map((row) =>
                    createMediaRow(
                        row,
                        nativeComponents,
                        onSelect,
                    ),
                ),
            );
        },
    };
}
