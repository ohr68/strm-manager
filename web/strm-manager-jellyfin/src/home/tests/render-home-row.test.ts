import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import {
    HomeRowItem,
    renderHomeRow,
} from '../render-home-row';

class ElementMock {
    className = '';
    textContent: string | null = null;
    readonly children: ElementMock[] = [];

    append(...children: ElementMock[]): void {
        this.children.push(...children);
    }

    appendChild(child: ElementMock): ElementMock {
        this.children.push(child);
        return child;
    }

    replaceChildren(...children: ElementMock[]): void {
        this.children.length = 0;
        this.children.push(...children);
    }
}

function createDocumentMock(): Pick<Document, 'createElement'> {
    return {
        createElement: () =>
            new ElementMock() as unknown as HTMLElement,
    } as Pick<Document, 'createElement'>;
}

function getChild(
    element: ElementMock,
    index: number,
): ElementMock {
    const child = element.children[index];

    if (!child) {
        throw new Error(
            `Expected child at index ${index}.`,
        );
    }

    return child;
}

describe('renderHomeRow', () => {
    const originalDocument = globalThis.document;

    beforeEach(() => {
        globalThis.document =
            createDocumentMock() as Document;
    });

    afterEach(() => {
        globalThis.document = originalDocument;
    });

    it('renders the native-style Jellyfin row structure', () => {
        const root = new ElementMock();

        renderHomeRow(
            root as unknown as HTMLElement,
            {
                title: 'STRM Manager',
                items: [
                    {
                        title: 'Joker',
                        subtitle: '2019',
                    },
                ],
            },
        );

        const section = getChild(root, 0);

        expect(section.className).toBe(
            'verticalSection emby-scroller-container',
        );

        const heading = getChild(section, 0);

        expect(heading.className).toBe(
            'sectionTitle sectionTitle-cards padded-left',
        );
        expect(heading.textContent).toBe(
            'STRM Manager',
        );

        const scroller = getChild(section, 1);

        expect(scroller.className).toBe(
            'padded-top-focusscale ' +
                'padded-bottom-focusscale ' +
                'emby-scroller',
        );

        const itemsContainer =
            getChild(scroller, 0);

        expect(itemsContainer.className).toBe(
            'itemsContainer scrollSlider ' +
                'focuscontainer-x animatedScrollX',
        );

        expect(itemsContainer.children).toHaveLength(1);
    });

    it('renders title and subtitle for an item', () => {
        const root = new ElementMock();

        renderHomeRow(
            root as unknown as HTMLElement,
            {
                title: 'STRM Manager',
                items: [
                    {
                        title: 'Joker',
                        subtitle: '2019',
                    },
                ],
            },
        );

        const card = getFirstCard(root);
        const cardBox = getChild(card, 0);

        expect(card.className).toBe(
            'card overflowPortraitCard card-hoverable',
        );

        const scalable = getChild(cardBox, 0);

        expect(scalable.className).toBe(
            'cardScalable',
        );

        expect(getChild(scalable, 0).className).toBe(
            'cardPadder cardPadder-overflowPortrait',
        );

        const title = getChild(cardBox, 1);

        expect(title.className).toBe(
            'cardText cardTextCentered cardText-first',
        );
        expect(title.textContent).toBe('Joker');

        const subtitle = getChild(cardBox, 2);

        expect(subtitle.className).toBe(
            'cardText cardTextCentered cardText-secondary',
        );
        expect(subtitle.textContent).toBe('2019');
    });

    it('omits the subtitle when it is not provided', () => {
        const root = new ElementMock();

        const item: HomeRowItem = {
            title: 'Joker',
        };

        renderHomeRow(
            root as unknown as HTMLElement,
            {
                title: 'STRM Manager',
                items: [item],
            },
        );

        const cardBox =
            getChild(getFirstCard(root), 0);

        expect(cardBox.children).toHaveLength(2);
    });

    it('replaces previous root content', () => {
        const root = new ElementMock();

        root.appendChild(new ElementMock());

        renderHomeRow(
            root as unknown as HTMLElement,
            {
                title: 'STRM Manager',
                items: [],
            },
        );

        expect(root.children).toHaveLength(1);
        expect(getChild(root, 0).className).toBe(
            'verticalSection emby-scroller-container',
        );
    });
});

function getFirstCard(
    root: ElementMock,
): ElementMock {
    const section = getChild(root, 0);
    const scroller = getChild(section, 1);
    const itemsContainer = getChild(scroller, 0);

    return getChild(itemsContainer, 0);
}
