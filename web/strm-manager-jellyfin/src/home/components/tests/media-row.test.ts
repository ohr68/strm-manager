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
    createNativeRowComponentsMock,
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
        const nativeComponents = createNativeRowComponentsMock();
        const section = createMediaRow({
            title: 'STRM Manager',
            items: [
                {
                    title: 'Joker',
                    subtitle: '2019',
                },
            ],
        }, nativeComponents) as unknown as ElementMock;

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
});
