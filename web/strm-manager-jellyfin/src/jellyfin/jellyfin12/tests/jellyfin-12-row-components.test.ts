import {
    afterEach,
    beforeEach,
    describe,
    expect,
    it,
    vi,
} from 'vitest';

import {
    createJellyfin12RowComponents,
} from '../jellyfin-12-row-components';

describe('createJellyfin12RowComponents', () => {
    const createElement = vi.fn(
        (tagName: string, is?: string) => ({
            tagName: tagName.toUpperCase(),
            is: is ?? null,
        }),
    );

    beforeEach(() => {
        createElement.mockClear();

        vi.stubGlobal(
            'document',
            {
                createElement,
            },
        );
    });

    afterEach(() => {
        vi.unstubAllGlobals();
    });

    it(
        'creates the scroller using the Jellyfin 12 legacy customized element signature',
        () => {
            const components =
                createJellyfin12RowComponents();

            const scroller =
                components.createScroller();

            expect(createElement).toHaveBeenCalledWith(
                'div',
                'emby-scroller',
            );

            expect(scroller).toEqual({
                tagName: 'DIV',
                is: 'emby-scroller',
            });
        },
    );
});
