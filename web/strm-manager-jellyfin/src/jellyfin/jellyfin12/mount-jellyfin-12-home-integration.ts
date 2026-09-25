import {
    resolveJellyfin12HomeHost,
} from './resolve-jellyfin-12-home-host';

const ROOT_ID = 'strm-manager-home-root';

export interface Jellyfin12HomeIntegration {
    readonly root: HTMLElement;
    unmount(): void;
}

export function mountJellyfin12HomeIntegration(
    documentRoot: Document = document,
): Jellyfin12HomeIntegration | null {
    const host =
        resolveJellyfin12HomeHost(documentRoot);

    if (!host) {
        return null;
    }

    const existingRoot =
        documentRoot.querySelector<HTMLElement>(
            `#${ROOT_ID}`,
        );

    if (existingRoot) {
        return {
            root: existingRoot,
            unmount: () => existingRoot.remove(),
        };
    }

    const root = documentRoot.createElement('div');

    root.id = ROOT_ID;
    root.setAttribute(
        'data-strm-manager-integration',
        'home',
    );

    if (host.firstSection) {
        host.container.insertBefore(
            root,
            host.firstSection,
        );
    } else {
        host.container.appendChild(root);
    }

    return {
        root,
        unmount: () => root.remove(),
    };
}
