import { FetchResourceOptions, FetchResourceResult } from "../http/fetcher";
import type { ContentHistoryResponse, ContentPageResponse, PageCollectionResponse, SectionsResponse } from "../types";
export type ResourceResult<T> = FetchResourceResult<T>;
export declare function fetchPageCollection(options?: FetchResourceOptions): Promise<ResourceResult<PageCollectionResponse>>;
export declare function fetchPageDetail(pageId: string, options?: FetchResourceOptions): Promise<ResourceResult<ContentPageResponse>>;
export declare function fetchSections(pageId: string, options?: FetchResourceOptions): Promise<ResourceResult<SectionsResponse>>;
export declare function fetchHistory(params?: {
    pageId?: string;
    take?: number;
}, options?: FetchResourceOptions): Promise<ResourceResult<ContentHistoryResponse>>;
export declare function invalidateContentCache(pageId?: string): void;
//# sourceMappingURL=content.d.ts.map