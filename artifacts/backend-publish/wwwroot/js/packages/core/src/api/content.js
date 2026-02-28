import { fetchResource, invalidateResource } from "../http/fetcher";
const CONTENT_BASE = "/api/content";
export async function fetchPageCollection(options = {}) {
    return fetchResource("content:pages", `${CONTENT_BASE}/pages`, options);
}
export async function fetchPageDetail(pageId, options = {}) {
    return fetchResource(`content:pages:${pageId}`, `${CONTENT_BASE}/pages/${pageId}`, options);
}
export async function fetchSections(pageId, options = {}) {
    const search = new URLSearchParams({ pageId });
    return fetchResource(`content:sections:${pageId}`, `${CONTENT_BASE}/sections?${search.toString()}`, options);
}
export async function fetchHistory(params = {}, options = {}) {
    const search = new URLSearchParams();
    if (params.pageId) {
        search.set("pageId", params.pageId);
    }
    if (params.take) {
        search.set("take", params.take.toString());
    }
    const key = `content:history:${params.pageId ?? "all"}${params.take ? `:${params.take}` : ""}`;
    const url = search.size > 0
        ? `${CONTENT_BASE}/history?${search.toString()}`
        : `${CONTENT_BASE}/history`;
    return fetchResource(key, url, options);
}
export function invalidateContentCache(pageId) {
    invalidateResource("content:pages");
    if (pageId) {
        invalidateResource(`content:pages:${pageId}`);
        invalidateResource(`content:sections:${pageId}`);
        invalidateResource(`content:history:${pageId}`);
    }
}
