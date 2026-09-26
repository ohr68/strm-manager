import type { NativeRowComponents } from '../native-row-components';
import {
    createMediaCard,
    type MediaCardModel,
    type MediaCardSelectHandler,
} from './media-card';

export interface MediaRowModel {
    readonly id?: string;
    readonly title: string;
    readonly items: readonly MediaCardModel[];
}

export function createMediaRow(
    row: MediaRowModel,
    nativeComponents: NativeRowComponents,
    onSelect?: MediaCardSelectHandler,
): HTMLElement {
    const section = document.createElement('div');

    section.className =
        'verticalSection emby-scroller-container';

    const heading = document.createElement('h2');

    heading.className =
        'sectionTitle sectionTitle-cards padded-left';
    heading.textContent = row.title;

    const scroller = nativeComponents.createScroller();

    scroller.setAttribute('data-centerfocus', 'true');
    scroller.setAttribute('data-scroll-mode-x', 'custom');

    scroller.className =
        'padded-top-focusscale ' +
        'padded-bottom-focusscale ' +
        'emby-scroller';

    const itemsContainer = nativeComponents.createItemsContainer();

    itemsContainer.className =
        'itemsContainer scrollSlider ' +
        'focuscontainer-x animatedScrollX';

    for (const item of row.items) {
        itemsContainer.appendChild(
            createMediaCard(item, onSelect),
        );
    }

    scroller.appendChild(itemsContainer);

    section.append(
        heading,
        scroller,
    );

    return section;
}
