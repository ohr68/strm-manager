import {
    mountJellyfin12HomeIntegration,
} from './mount-jellyfin-12-home-integration';

export interface Jellyfin12HomeIntegrationLifecycle {
    stop(): void;
}

export type Jellyfin12HomeIntegrationMounted =
    (root: HTMLElement) => void;

export function startJellyfin12HomeIntegration(
    onMounted: Jellyfin12HomeIntegrationMounted,
    documentRoot: Document = document,
): Jellyfin12HomeIntegrationLifecycle | null {
    const reactRoot =
        documentRoot.querySelector('#reactRoot');

    if (!reactRoot) {
        return null;
    }

    let currentRoot: HTMLElement | null = null;

    const reconcile = (): void => {
        const integration =
            mountJellyfin12HomeIntegration(
                documentRoot,
            );

        if (
            !integration ||
            integration.root === currentRoot
        ) {
            return;
        }

        currentRoot = integration.root;
        onMounted(integration.root);
    };

    reconcile();

    const observer =
        new MutationObserver(() => {
            reconcile();
        });

    observer.observe(
        reactRoot,
        {
            childList: true,
            subtree: true,
        },
    );

    return {
        stop: () => observer.disconnect(),
    };
}
