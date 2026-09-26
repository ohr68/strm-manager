import {
    afterEach,
    beforeEach,
    describe,
    expect,
    it,
} from 'vitest';

import {
    createHomeView,
} from '../home-view';

import {
    createDocumentMock,
    createNativeRowComponentsMock,
    ElementMock,
    getChild,
} from './dom-test-utils';

describe('createHomeView', () => {
    const originalDocument =
        globalThis.document;

    beforeEach(() => {
        globalThis.document =
            createDocumentMock() as Document;
    });

    afterEach(() => {
        globalThis.document =
            originalDocument;
    });

    it('renders loading skeleton rows', () => {
        const root =
            new ElementMock();

        const nativeComponents =
            createNativeRowComponentsMock();

        const view =
            createHomeView(
                nativeComponents,
            );

        view.renderLoading(
            root as unknown as HTMLElement,
        );

        expect(root.children)
            .toHaveLength(2);

        const firstRow =
            getChild(root, 0);

        expect(firstRow.className)
            .toContain(
                'strm-manager-skeleton-row',
            );

        expect(
            firstRow.attributes.get(
                'aria-hidden',
            ),
        ).toBe('true');

        const heading =
            getChild(firstRow, 0);

        expect(heading.className)
            .toContain(
                'strm-manager-skeleton-heading',
            );

        expect(heading.className)
            .toContain(
                'strm-manager-skeleton-surface',
            );

        const scroller =
            getChild(firstRow, 1);

        const items =
            getChild(scroller, 0);

        expect(items.children)
            .toHaveLength(6);

        const firstCard =
            getChild(items, 0);

        expect(firstCard.className)
            .toBe(
                'strm-manager-skeleton-card',
            );

        expect(firstCard.children)
            .toHaveLength(3);

        expect(
            getChild(
                firstCard,
                0,
            ).className,
        ).toContain(
            'strm-manager-skeleton-poster',
        );
    });

    it('renders multiple rows', () => {
        const root =
            new ElementMock();

        const nativeComponents =
            createNativeRowComponentsMock();

        const view =
            createHomeView(
                nativeComponents,
            );

        view.render(
            root as unknown as HTMLElement,
            [
                {
                    title: 'Popular',
                    items: [],
                },
                {
                    title: 'Action',
                    items: [],
                },
            ],
        );

        expect(root.children)
            .toHaveLength(2);

        expect(
            getChild(
                getChild(root, 0),
                0,
            ).textContent,
        ).toBe('Popular');

        expect(
            getChild(
                getChild(root, 1),
                0,
            ).textContent,
        ).toBe('Action');
    });

    it('replaces loading skeleton with catalog rows', () => {
        const root =
            new ElementMock();

        const nativeComponents =
            createNativeRowComponentsMock();

        const view =
            createHomeView(
                nativeComponents,
            );

        view.renderLoading(
            root as unknown as HTMLElement,
        );

        expect(root.children)
            .toHaveLength(2);

        expect(
            getChild(root, 0).className,
        ).toContain(
            'strm-manager-skeleton-row',
        );

        view.render(
            root as unknown as HTMLElement,
            [
                {
                    title: 'Popular',
                    items: [],
                },
            ],
        );

        expect(root.children)
            .toHaveLength(1);

        expect(
            getChild(
                getChild(root, 0),
                0,
            ).textContent,
        ).toBe('Popular');

        expect(
            getChild(root, 0).className,
        ).not.toContain(
            'strm-manager-skeleton-row',
        );
    });

    it('replaces previous content', () => {
        const root =
            new ElementMock();

        root.appendChild(
            new ElementMock(),
        );

        const nativeComponents =
            createNativeRowComponentsMock();

        createHomeView(
            nativeComponents,
        ).render(
            root as unknown as HTMLElement,
            [
                {
                    title: 'STRM Manager',
                    items: [],
                },
            ],
        );

        expect(root.children)
            .toHaveLength(1);

        expect(
            getChild(
                getChild(root, 0),
                0,
            ).textContent,
        ).toBe('STRM Manager');
    });
});
