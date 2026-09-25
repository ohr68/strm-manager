export interface Jellyfin12SystemInfo {
    readonly Version?: string;
    readonly ProductName?: string;
}

export interface Jellyfin12ApiClient {
    getCurrentUserId(): string | null;
    getSystemInfo(): Promise<Jellyfin12SystemInfo>;
}

export interface Jellyfin12Dashboard {
    navigate(url: string): void;
}

export interface Jellyfin12Globals {
    readonly ApiClient?: Jellyfin12ApiClient;
    readonly Dashboard?: Jellyfin12Dashboard;
}

export function getJellyfin12Globals(): Jellyfin12Globals {
    if (typeof window === 'undefined') {
        return {};
    }

    return window as typeof window & Jellyfin12Globals;
}