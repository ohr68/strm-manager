export interface AppConfig {
    readonly strmManagerBaseUrl: string;
}

export function getAppConfig(): AppConfig | null {
    const strmManagerBaseUrl =
        import.meta.env.VITE_STRM_MANAGER_BASE_URL?.trim();

    if (!strmManagerBaseUrl) {
        return null;
    }

    return {
        strmManagerBaseUrl,
    };
}