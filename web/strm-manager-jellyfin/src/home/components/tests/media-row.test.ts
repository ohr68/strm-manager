import {
    afterEach,
    beforeEach,
    describe,
    expect,
    it,
} from 'vitest';
import { createMediaRow } from '../media-row';
import {
    createDocumentMock,
    ElementMock,
    getChild,
} from '../../tests/dom-test-utils';

describe('createMediaRow', () => {
    const originalDocument = globalThis.document;

    beforeEach(() => {
        globalThis.document =
            createDocumentMock() as Document;
    });

    afterEach(() => {
        globalThis.document = originalDocument;
    });

    it('renders native-style Jellyfin row structure', () => {
        const section = createMediaRow({
            title: 'STRM Manager',
            items: [
                {
                    title: 'Joker',
                    subtitle: '2019',
                },
            ],
        }) as unknown as ElementMock;

        expect(section.className).toBe(
            'verticalSection',
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
                'padded-bottom-focusscale',
        );

        const itemsContainer =
            getChild(scroller, 0);

        expect(itemsContainer.className).toBe(
            'itemsContainer scrollSlider ' +
                'focuscontainer-x animatedScrollX',
        );

        expect(itemsContainer.children).toHaveLength(1);
    });
});
