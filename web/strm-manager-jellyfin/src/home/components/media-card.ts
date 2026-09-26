export interface MediaCardModel {
    readonly id?: string;
    readonly title: string;
    readonly subtitle?: string;
    readonly imageUrl?: string;
}

export type MediaCardSelectHandler = (
    item: MediaCardModel,
) => void;

export function createMediaCard(
    item: MediaCardModel,
    onSelect?: MediaCardSelectHandler,
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

        image.setAttribute('role', 'img');
        image.setAttribute('aria-label', item.title);
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

    if (onSelect) {
        card.addEventListener('click', () => {
            onSelect(item);
        });
    }

    return card;
}
