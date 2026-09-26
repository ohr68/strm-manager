const SKELETON_CARD_COUNT = 6;

export function createMediaRowSkeleton():
    HTMLElement {
    const row = document.createElement('section');

    row.className =
        'verticalSection ' +
        'emby-scroller-container ' +
        'strm-manager-skeleton-row';

    row.setAttribute(
        'aria-hidden',
        'true',
    );

    const heading =
        document.createElement('div');

    heading.className =
        'strm-manager-skeleton-heading ' +
        'strm-manager-skeleton-surface';

    row.appendChild(heading);

    const scroller =
        document.createElement('div');

    scroller.className =
        'strm-manager-skeleton-scroller';

    const items =
        document.createElement('div');

    items.className =
        'strm-manager-skeleton-items';

    for (
        let index = 0;
        index < SKELETON_CARD_COUNT;
        index += 1
    ) {
        const card =
            document.createElement('div');

        card.className =
            'strm-manager-skeleton-card';

        const poster =
            document.createElement('div');

        poster.className =
            'strm-manager-skeleton-poster ' +
            'strm-manager-skeleton-surface';

        const title =
            document.createElement('div');

        title.className =
            'strm-manager-skeleton-title ' +
            'strm-manager-skeleton-surface';

        const subtitle =
            document.createElement('div');

        subtitle.className =
            'strm-manager-skeleton-subtitle ' +
            'strm-manager-skeleton-surface';

        card.append(
            poster,
            title,
            subtitle,
        );

        items.appendChild(card);
    }

    scroller.appendChild(items);

    row.appendChild(scroller);

    return row;
}
