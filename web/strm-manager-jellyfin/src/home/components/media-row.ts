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
    onSelect?: MediaCardSelectHandler,
): HTMLElement {
    const section = document.createElement('div');

    section.className =
        'verticalSection';

    const heading = document.createElement('h2');

    heading.className =
        'sectionTitle sectionTitle-cards padded-left';
    heading.textContent = row.title;

    const scroller = document.createElement('div');

    scroller.className =
        'padded-top-focusscale ' +
        'padded-bottom-focusscale';

    const itemsContainer = document.createElement('div');

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
