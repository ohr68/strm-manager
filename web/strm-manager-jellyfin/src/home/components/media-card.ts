export interface MediaCardModel {
    readonly id?: string;
    readonly title: string;
    readonly subtitle?: string;
    readonly imageUrl?: string;
}

export interface MediaCardInteraction {
    setPreparing(preparing: boolean): void;
}

export type MediaCardSelectHandler = (
    item: MediaCardModel,
    interaction: MediaCardInteraction,
) => void;

const PREPARING_CLASS =
    'strm-manager-card--preparing';

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
        const image =
            document.createElement('div');

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

    const overlay =
        document.createElement('div');

    overlay.className =
        'cardOverlayContainer';

    const playButton =
        document.createElement('button');

    playButton.type = 'button';

    playButton.className =
        'cardOverlayButton ' +
        'cardOverlayButton-hover ' +
        'paper-icon-button-light ' +
        'cardOverlayFab-primary';

    playButton.setAttribute(
        'aria-label',
        `Preparar ${item.title}`,
    );

    const playIcon =
        document.createElement('span');

    playIcon.className =
        'material-icons ' +
        'cardOverlayButtonIcon ' +
        'cardOverlayButtonIcon-hover';

    playIcon.textContent = 'play_arrow';

    playIcon.setAttribute(
        'aria-hidden',
        'true',
    );

    const spinner =
        document.createElement('span');

    spinner.className =
        'strm-manager-card-spinner';

    spinner.setAttribute(
        'aria-hidden',
        'true',
    );

    playButton.append(
        playIcon,
        spinner,
    );

    overlay.appendChild(playButton);
    scalable.appendChild(overlay);

    const title =
        document.createElement('div');

    title.className =
        'cardText cardTextCentered cardText-first';

    title.textContent = item.title;

    cardBox.append(
        scalable,
        title,
    );

    if (item.subtitle) {
        const subtitle =
            document.createElement('div');

        subtitle.className =
            'cardText ' +
            'cardTextCentered ' +
            'cardText-secondary';

        subtitle.textContent =
            item.subtitle;

        cardBox.appendChild(subtitle);
    }

    card.appendChild(cardBox);

    let preparing = false;

    const setPreparing = (
        nextPreparing: boolean,
    ): void => {
        preparing = nextPreparing;

        card.setAttribute(
            'aria-busy',
            preparing ? 'true' : 'false',
        );

        playButton.disabled = preparing;

        if (preparing) {
            card.classList.add(
                PREPARING_CLASS,
            );
        } else {
            card.classList.remove(
                PREPARING_CLASS,
            );
        }

        playButton.setAttribute(
            'aria-label',
            preparing
                ? `Preparando ${item.title}`
                : `Preparar ${item.title}`,
        );
    };

    if (onSelect) {
        card.addEventListener(
            'click',
            () => {
                if (preparing) {
                    return;
                }

                onSelect(
                    item,
                    {
                        setPreparing,
                    },
                );
            },
        );
    }

    return card;
}
