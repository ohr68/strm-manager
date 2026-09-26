import type {
    NativeRowComponents,
} from '../../home/native-row-components';

type LegacyCreateElement = (
    tagName: string,
    is: string,
) => HTMLElement;

function createCustomizedElement(
    tagName: string,
    is: string,
): HTMLElement {
    const createElement =
        document.createElement.bind(
            document,
        ) as LegacyCreateElement;

    return createElement(tagName, is);
}

export function createJellyfin12RowComponents():
    NativeRowComponents {
    return {
        createScroller(): HTMLElement {
            return createCustomizedElement(
                'div',
                'emby-scroller',
            );
        },

        createItemsContainer(): HTMLElement {
            return createCustomizedElement(
                'div',
                'emby-itemscontainer',
            );
        },
    };
}
