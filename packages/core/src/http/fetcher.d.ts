export interface FetchResourceOptions extends RequestInit {
    ttlMs?: number;
    revalidate?: boolean;
}
export interface FetchResourceResult<T> {
    data: T;
    etag?: string | null;
    fromCache: boolean;
}
export declare function fetchResource<T>(cacheKey: string, input: RequestInfo | URL, options?: FetchResourceOptions): Promise<FetchResourceResult<T>>;
export declare function invalidateResource(cacheKey: string): void;
export declare function getCachedResource<T>(cacheKey: string): T | undefined;
export declare function setCachedResource<T>(cacheKey: string, data: T, etag?: string | null): void;
//# sourceMappingURL=fetcher.d.ts.map