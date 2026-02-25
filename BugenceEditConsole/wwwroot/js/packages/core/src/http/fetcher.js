const resourceCache = new Map();
const DEFAULT_TTL_MS = 15_000;
export async function fetchResource(cacheKey, input, options = {}) {
    const now = Date.now();
    const ttlMs = options.ttlMs ?? DEFAULT_TTL_MS;
    const cached = resourceCache.get(cacheKey);
    if (!options.revalidate && cached && now - cached.timestamp <= ttlMs) {
        return { data: cached.data, etag: cached.etag, fromCache: true };
    }
    const headers = new Headers(options.headers ?? {});
    if (cached?.etag) {
        headers.set("If-None-Match", cached.etag);
    }
    const response = await fetch(input, {
        ...options,
        headers
    });
    if (response.status === 304 && cached) {
        cached.timestamp = now;
        resourceCache.set(cacheKey, cached);
        return { data: cached.data, etag: cached.etag, fromCache: true };
    }
    if (!response.ok) {
        throw new Error(await extractErrorMessage(response));
    }
    const data = (await response.json());
    const etag = response.headers.get("ETag");
    resourceCache.set(cacheKey, {
        data,
        etag,
        timestamp: now
    });
    return { data, etag: etag ?? undefined, fromCache: false };
}
export function invalidateResource(cacheKey) {
    resourceCache.delete(cacheKey);
}
export function getCachedResource(cacheKey) {
    return resourceCache.get(cacheKey)?.data;
}
export function setCachedResource(cacheKey, data, etag) {
    resourceCache.set(cacheKey, {
        data,
        etag,
        timestamp: Date.now()
    });
}
async function extractErrorMessage(response) {
    try {
        const payload = await response.clone().json();
        if (typeof payload?.message === "string") {
            return payload.message;
        }
        if (typeof payload?.Message === "string") {
            return payload.Message;
        }
    }
    catch {
        // ignore json parse failures
    }
    const text = await response.text();
    if (text) {
        return text;
    }
    return `Request failed with status ${response.status}`;
}
