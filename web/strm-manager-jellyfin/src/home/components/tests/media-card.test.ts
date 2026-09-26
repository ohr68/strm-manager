import {
    afterEach,
    beforeEach,
    describe,
    expect,
    it,
} from 'vitest';
import { createMediaCard } from '../media-card';
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
});
