import {
    createMediaCard,
    type MediaCardModel,
} from './media-card';

export interface MediaRowModel {
    readonly id?: string;
    readonly title: string;
    readonly items: readonly MediaCardModel[];
}

export function createMediaRow(
    row: MediaRowModel,
): HTMLElement {
    const section = document.createElement('div');

    section.className =
        'verticalSection emby-scroller-container';

    const heading = document.createElement('h2');

    heading.className =
        'sectionTitle sectionTitle-cards padded-left';
    heading.textContent = row.title;

    const scroller = document.createElement('div');

    scroller.className =
        'padded-top-focusscale ' +
        'padded-bottom-focusscale ' +
        'emby-scroller';

    const itemsContainer = document.createElement('div');

    itemsContainer.className =
        'itemsContainer scrollSlider ' +
        'focuscontainer-x animatedScrollX';

    for (const item of row.items) {
        itemsContainer.appendChild(
            createMediaCard(item),
        );
    }

    scroller.appendChild(itemsContainer);

    section.append(
        heading,
        scroller,
    );

    return section;
}
