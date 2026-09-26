export interface HomeRowItem {
    readonly title: string;
    readonly subtitle?: string;
    readonly imageUrl?: string;
}

export interface RenderHomeRowOptions {
    readonly title: string;
    readonly items: readonly HomeRowItem[];
}

export function renderHomeRow(
    root: HTMLElement,
    options: RenderHomeRowOptions,
): void {
    const section = document.createElement('div');

    section.className =
        'verticalSection emby-scroller-container';

    const heading = document.createElement('h2');

    heading.className =
        'sectionTitle sectionTitle-cards padded-left';
    heading.textContent = options.title;

    const scroller = document.createElement('div');

    scroller.className =
        'padded-top-focusscale ' +
        'padded-bottom-focusscale ' +
        'emby-scroller';

    const itemsContainer = document.createElement('div');

    itemsContainer.className =
        'itemsContainer scrollSlider ' +
        'focuscontainer-x animatedScrollX';

    for (const item of options.items) {
        itemsContainer.appendChild(
            renderHomeRowItem(item),
        );
    }

    scroller.appendChild(itemsContainer);

    section.append(
        heading,
        scroller,
    );

    root.replaceChildren(section);
}

function renderHomeRowItem(
    item: HomeRowItem,
): HTMLElement {
    const card = document.createElement('div');

    card.className =
        'card overflowPortraitCard card-hoverable';

    const cardBox = document.createElement('div');

    cardBox.className =
        'cardBox cardBox-bottompadded';

    const scalable = document.createElement('div');

    scalable.className = 'cardScalable';

    const padder = document.createElement('div');

    padder.className =
        'cardPadder cardPadder-overflowPortrait';

    scalable.appendChild(padder);

    if (item.imageUrl) {
        const image = document.createElement('div');

        image.className =
            'cardImageContainer coveredImage cardContent';

        image.setAttribute(
            'role',
            'img',
        );

        image.setAttribute(
            'aria-label',
            item.title,
        );

        image.style.backgroundImage =
            `url("${item.imageUrl}")`;

        scalable.appendChild(image);
    }

    const title = document.createElement('div');

    title.className =
        'cardText cardTextCentered cardText-first';
    title.textContent = item.title;

    cardBox.append(
        scalable,
        title,
    );

    if (item.subtitle) {
        const subtitle = document.createElement('div');

        subtitle.className =
            'cardText cardTextCentered cardText-secondary';
        subtitle.textContent = item.subtitle;

        cardBox.appendChild(subtitle);
    }

    card.appendChild(cardBox);

    return card;
}
