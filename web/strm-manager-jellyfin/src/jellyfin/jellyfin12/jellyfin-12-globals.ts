import type { Jellyfin12WebpackGlobals } from './resolve-jellyfin-12-playback-manager';

export interface Jellyfin12SystemInfo {
    readonly Version?: string;
    readonly ProductName?: string;
}

export interface Jellyfin12ServerInfo {
    readonly Id?: string;
}

export interface Jellyfin12Item {
    readonly Id?: string;
    readonly Name?: string;
    readonly Type?: string;
    readonly MediaType?: string;
    readonly ProductionYear?: number;
}

export interface Jellyfin12AjaxOptions {
    readonly type: 'GET' | 'POST';
    readonly url: string;
    readonly dataType: 'json';
    readonly data?: unknown;
}

export interface Jellyfin12ApiClient {
    getCurrentUserId(): string | null;
    getSystemInfo(): Promise<Jellyfin12SystemInfo>;
    getItem?(
        userId: string | null,
        itemId: string,
    ): Promise<Jellyfin12Item>;
    serverInfo(): Jellyfin12ServerInfo;
    getUrl?(path: string): string;
    ajax?<T>(
        options: Jellyfin12AjaxOptions,
    ): Promise<T>;
}

export interface Jellyfin12Dashboard {
    navigate(url: string): void;
}

export interface Jellyfin12Globals extends Jellyfin12WebpackGlobals {
    readonly ApiClient?: Jellyfin12ApiClient;
    readonly Dashboard?: Jellyfin12Dashboard;
}

export function getJellyfin12Globals(): Jellyfin12Globals {
    if (typeof window === 'undefined') {
        return {};
    }

    return window as typeof window & Jellyfin12Globals;
}