export interface Jellyfin12HomeHost {
    readonly container: HTMLElement;
    readonly firstSection: Element | null;
}

export function resolveJellyfin12HomeHost(
    documentRoot: Document = document,
): Jellyfin12HomeHost | null {
    const indexPage =
        documentRoot.querySelector('#indexPage');

    if (!indexPage) {
        return null;
    }

    const homeTab =
        indexPage.querySelector(':scope > #homeTab');

    if (
        !homeTab ||
        !homeTab.classList.contains('is-active')
    ) {
        return null;
    }

    const container =
        homeTab.querySelector<HTMLElement>(
            ':scope > .sections.homeSectionsContainer',
        );

    if (!container) {
        return null;
    }

    return {
        container,
        firstSection: container.firstElementChild,
    };
}
