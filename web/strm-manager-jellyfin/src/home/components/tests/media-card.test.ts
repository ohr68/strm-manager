import {
    afterEach,
    beforeEach,
    describe,
    expect,
    it,
} from 'vitest';

import {
    createMediaCard,
    type MediaCardInteraction,
    type MediaCardModel,
} from '../media-card';

import {
    createDocumentMock,
    ElementMock,
    getChild,
} from '../../tests/dom-test-utils';

describe('createMediaCard', () => {
    const originalDocument = globalThis.document;

    beforeEach(() => {
        globalThis.document =
            createDocumentMock() as Document;
    });

    afterEach(() => {
        globalThis.document = originalDocument;
    });

    it('renders native portrait card geometry', () => {
        const element = createMediaCard({
            title: 'Joker',
            subtitle: '2019',
        }) as unknown as ElementMock;

        expect(element.className).toBe(
            'card overflowPortraitCard card-hoverable',
        );

        const cardBox = getChild(element, 0);
        const scalable = getChild(cardBox, 0);

        expect(scalable.className).toBe(
            'cardScalable',
        );

        expect(
            getChild(scalable, 0).className,
        ).toBe(
            'cardPadder cardPadder-overflowPortrait',
        );
    });

    it('renders title and subtitle', () => {
        const card = createMediaCard({
            title: 'Joker',
            subtitle: '2019',
        });

        const element =
            card as unknown as ElementMock;

        const cardBox = getChild(element, 0);

        expect(
            getChild(cardBox, 1).textContent,
        ).toBe('Joker');

        expect(
            getChild(cardBox, 2).textContent,
        ).toBe('2019');
    });

    it('omits subtitle when it is not provided', () => {
        const card = createMediaCard({
            title: 'Joker',
        });

        const element =
            card as unknown as ElementMock;

        expect(
            getChild(element, 0).children,
        ).toHaveLength(2);
    });

    it('renders artwork when image URL is provided', () => {
        const card = createMediaCard({
            title: 'Joker',
            imageUrl: '/joker.jpg',
        });

        const element =
            card as unknown as ElementMock;

        const cardBox = getChild(element, 0);
        const scalable = getChild(cardBox, 0);
        const image = getChild(scalable, 1);

        expect(image.className).toBe(
            'cardImageContainer coveredImage cardContent',
        );

        expect(
            image.attributes.get('role'),
        ).toBe('img');

        expect(
            image.attributes.get('aria-label'),
        ).toBe('Joker');

        expect(
            image.style.backgroundImage,
        ).toBe('url("/joker.jpg")');
    });

    it('renders native-style hover overlay', () => {
        const card = createMediaCard({
            title: 'Joker',
            imageUrl: '/joker.jpg',
        });

        const element =
            card as unknown as ElementMock;

        const cardBox = getChild(element, 0);
        const scalable = getChild(cardBox, 0);
        const overlay = getChild(scalable, 2);

        expect(overlay.className).toBe(
            'cardOverlayContainer',
        );

        const playButton = getChild(overlay, 0);

        expect(playButton.className).toBe(
            'cardOverlayButton ' +
            'cardOverlayButton-hover ' +
            'paper-icon-button-light ' +
            'cardOverlayFab-primary',
        );

        expect(
            playButton.attributes.get('aria-label'),
        ).toBe('Preparar Joker');

        const playIcon = getChild(playButton, 0);
        const spinner = getChild(playButton, 1);

        expect(playIcon.className).toBe(
            'material-icons ' +
            'cardOverlayButtonIcon ' +
            'cardOverlayButtonIcon-hover',
        );

        expect(playIcon.textContent).toBe(
            'play_arrow',
        );

        expect(
            playIcon.attributes.get('aria-hidden'),
        ).toBe('true');

        expect(spinner.className).toBe(
            'strm-manager-card-spinner',
        );

        expect(
            spinner.attributes.get('aria-hidden'),
        ).toBe('true');
    });

    it('renders hover overlay without artwork', () => {
        const card = createMediaCard({
            title: 'Joker',
        });

        const element =
            card as unknown as ElementMock;

        const cardBox = getChild(element, 0);
        const scalable = getChild(cardBox, 0);
        const overlay = getChild(scalable, 1);

        expect(overlay.className).toBe(
            'cardOverlayContainer',
        );
    });

    it('notifies when the card is selected', () => {
        const item = {
            id: 'tt7286456',
            title: 'Joker',
            subtitle: '2019',
        };

        const selections: MediaCardModel[] = [];

        createMediaCard(
            item,
            (selected) => {
                selections.push(selected);
            },
        ).click();

        expect(selections).toEqual([item]);
    });

    it('exposes preparing visual state on selection', () => {
        let interaction:
            MediaCardInteraction | undefined;

        const card = createMediaCard(
            {
                title: 'Joker',
                imageUrl: '/joker.jpg',
            },
            (_item, selectedInteraction) => {
                interaction =
                    selectedInteraction;
            },
        );

        card.click();

        expect(interaction).toBeDefined();

        const element =
            card as unknown as ElementMock;

        const cardBox = getChild(element, 0);
        const scalable = getChild(cardBox, 0);
        const overlay = getChild(scalable, 2);
        const playButton = getChild(overlay, 0);
        const playIcon = getChild(playButton, 0);
        const spinner = getChild(playButton, 1);

        interaction?.setPreparing(true);

        expect(
            element.attributes.get('aria-busy'),
        ).toBe('true');

        expect(playButton.disabled).toBe(true);

        expect(
            playButton.attributes.get('aria-label'),
        ).toBe('Preparando Joker');

        expect(element.className).toContain(
            'strm-manager-card--preparing',
        );

        expect(playIcon.textContent).toBe(
            'play_arrow',
        );

        expect(spinner.className).toBe(
            'strm-manager-card-spinner',
        );

        interaction?.setPreparing(false);

        expect(
            element.attributes.get('aria-busy'),
        ).toBe('false');

        expect(playButton.disabled).toBe(false);

        expect(
            playButton.attributes.get('aria-label'),
        ).toBe('Preparar Joker');

        expect(element.className).not.toContain(
            'strm-manager-card--preparing',
        );

        expect(playIcon.textContent).toBe(
            'play_arrow',
        );
    });

    it('ignores repeated selection while preparing', () => {
        let selections = 0;

        let interaction:
            MediaCardInteraction | undefined;

        const card = createMediaCard(
            {
                title: 'Joker',
            },
            (_item, selectedInteraction) => {
                selections += 1;
                interaction =
                    selectedInteraction;
            },
        );

        card.click();

        interaction?.setPreparing(true);

        card.click();
        card.click();

        expect(selections).toBe(1);

        interaction?.setPreparing(false);

        card.click();

        expect(selections).toBe(2);
    });
});
