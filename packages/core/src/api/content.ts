import {
  fetchResource,
  FetchResourceOptions,
  FetchResourceResult,
  invalidateResource
} from "../http/fetcher";
import type {
  ContentHistoryResponse,
  ContentPageResponse,
  PageCollectionResponse,
  SectionsResponse
} from "../types";

const CONTENT_BASE = "/api/content";

export type ResourceResult<T> = FetchResourceResult<T>;

export async function fetchPageCollection(
  options: FetchResourceOptions = {}
): Promise<ResourceResult<PageCollectionResponse>> {
  return fetchResource<PageCollectionResponse>(
    "content:pages",
    `${CONTENT_BASE}/pages`,
    options
  );
}

export async function fetchPageDetail(
  pageId: string,
  options: FetchResourceOptions = {}
): Promise<ResourceResult<ContentPageResponse>> {
  return fetchResource<ContentPageResponse>(
    `content:pages:${pageId}`,
    `${CONTENT_BASE}/pages/${pageId}`,
    options
  );
}

export async function fetchSections(
  pageId: string,
  options: FetchResourceOptions = {}
): Promise<ResourceResult<SectionsResponse>> {
  const search = new URLSearchParams({ pageId });
  return fetchResource<SectionsResponse>(
    `content:sections:${pageId}`,
    `${CONTENT_BASE}/sections?${search.toString()}`,
    options
  );
}

export async function fetchHistory(
  params: {
    pageId?: string;
    take?: number;
  } = {},
  options: FetchResourceOptions = {}
): Promise<ResourceResult<ContentHistoryResponse>> {
  const search = new URLSearchParams();
  if (params.pageId) {
    search.set("pageId", params.pageId);
  }
  if (params.take) {
    search.set("take", params.take.toString());
  }

  const key = `content:history:${params.pageId ?? "all"}${
    params.take ? `:${params.take}` : ""
  }`;

  const url =
    search.size > 0
      ? `${CONTENT_BASE}/history?${search.toString()}`
      : `${CONTENT_BASE}/history`;

  return fetchResource<ContentHistoryResponse>(key, url, options);
}

export function invalidateContentCache(pageId?: string) {
  invalidateResource("content:pages");
  if (pageId) {
    invalidateResource(`content:pages:${pageId}`);
    invalidateResource(`content:sections:${pageId}`);
    invalidateResource(`content:history:${pageId}`);
  }
}

