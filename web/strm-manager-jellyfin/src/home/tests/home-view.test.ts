import {
    afterEach,
    beforeEach,
    describe,
    expect,
    it,
} from 'vitest';
import { createHomeView } from '../home-view';
import {
    createDocumentMock,
    createNativeRowComponentsMock,
    ElementMock,
    getChild,
} from './dom-test-utils';

describe('createHomeView', () => {
    const originalDocument = globalThis.document;

    beforeEach(() => {
        globalThis.document =
            createDocumentMock() as Document;
    });

    afterEach(() => {
        globalThis.document = originalDocument;
    });

    it('renders multiple rows', () => {
        const root = new ElementMock();
        const nativeComponents = createNativeRowComponentsMock();
        const view = createHomeView(nativeComponents);

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

        expect(root.children).toHaveLength(2);

        expect(
            getChild(getChild(root, 0), 0).textContent,
        ).toBe('Popular');

        expect(
            getChild(getChild(root, 1), 0).textContent,
        ).toBe('Action');
    });

    it('replaces previous content', () => {
        const root = new ElementMock();

        root.appendChild(new ElementMock());

        const nativeComponents = createNativeRowComponentsMock();

        createHomeView(nativeComponents).render(
            root as unknown as HTMLElement,
            [
                {
                    title: 'STRM Manager',
                    items: [],
                },
            ],
        );

        expect(root.children).toHaveLength(1);

        expect(
            getChild(getChild(root, 0), 0).textContent,
        ).toBe('STRM Manager');
    });
});
