import type { MediaCardModel } from './components/media-card';
import {
    createMediaRow,
    type MediaRowModel,
} from './components/media-row';
import type { NativeRowComponents } from './native-row-components';

export interface HomeView {
    render(
        root: HTMLElement,
        rows: readonly MediaRowModel[],
        onSelect?: (item: MediaCardModel) => void,
    ): void;
}

export function createHomeView(
    nativeComponents: NativeRowComponents,
): HomeView {
    return {
        render(root, rows, onSelect): void {
            root.replaceChildren(
                ...rows.map((row) =>
                    createMediaRow(
                        row,
                        nativeComponents,
                        onSelect
                    ),
                ),
            );
        }
    };
}
