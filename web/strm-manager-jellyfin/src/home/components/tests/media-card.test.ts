import {
    afterEach,
    beforeEach,
    describe,
    expect,
    it,
} from 'vitest';
import {
    createMediaCard,
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

        expect(getChild(scalable, 0).className).toBe(
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

        expect(getChild(cardBox, 1).textContent).toBe(
            'Joker',
        );

        expect(getChild(cardBox, 2).textContent).toBe(
            '2019',
        );
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
        expect(image.attributes.get('role')).toBe('img');
        expect(
            image.attributes.get('aria-label'),
        ).toBe('Joker');
        expect(image.style.backgroundImage).toBe(
            'url("/joker.jpg")',
        );
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

        // padder = 0
        // artwork = 1
        // overlay = 2
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

        expect(playIcon.className).toBe(
            'material-icons ' +
            'cardOverlayButtonIcon ' +
            'cardOverlayButtonIcon-hover ' +
            'play_arrow',
        );

        expect(
            playIcon.attributes.get('aria-hidden'),
        ).toBe('true');
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

        expect(playIcon.className).toBe(
            'material-icons ' +
            'cardOverlayButtonIcon ' +
            'cardOverlayButtonIcon-hover ' +
            'play_arrow',
        );

        expect(
            playIcon.attributes.get('aria-hidden'),
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

        const card = createMediaCard(item, (selected) => {
            selections.push(selected);
        });

        card.click();

        expect(selections).toEqual([item]);
    });
});
