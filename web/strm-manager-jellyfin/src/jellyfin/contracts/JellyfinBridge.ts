export interface JellyfinVersion {
    readonly major: number;
    readonly minor: number;
    readonly patch: number;
    readonly raw: string;
}

export interface JellyfinCapabilities {
    readonly authenticatedUser: boolean;
    readonly navigation: boolean;
    readonly itemDetails: boolean;
    readonly playback: boolean;
    readonly homeIntegration: boolean;
}

export interface JellyfinItemRef {
    readonly id: string;
}

export interface JellyfinAuth {
    getCurrentUserId(): string | null;
}

export interface JellyfinNavigation {
    openItem(item: JellyfinItemRef): Promise<void>;
    openHome(): Promise<void>;
}

export interface JellyfinPlayback {
    play(item: JellyfinItemRef): Promise<void>;
}

export interface JellyfinBridge {
    readonly version: JellyfinVersion;
    readonly capabilities: JellyfinCapabilities;
    readonly auth: JellyfinAuth;
    readonly navigation: JellyfinNavigation;
    readonly playback: JellyfinPlayback;
}