function stableStringify(value) {
    if (value === null || typeof value !== "object") {
        return JSON.stringify(value);
    }
    if (Array.isArray(value)) {
        return `[${value.map((entry) => stableStringify(entry)).join(",")}]`;
    }
    const entries = Object.entries(value).sort(([a], [b]) => a.localeCompare(b));
    return `{${entries
        .map(([key, entry]) => `${JSON.stringify(key)}:${stableStringify(entry)}`)
        .join(",")}}`;
}
function hashPayload(section) {
    const payload = {
        id: section.id,
        sectionKey: section.sectionKey,
        contentType: section.contentType,
        contentValue: section.contentValue ?? null,
        mediaPath: section.mediaPath ?? null,
        mediaAltText: section.mediaAltText ?? null,
        displayOrder: section.displayOrder,
        isLocked: section.isLocked,
        updatedAtUtc: section.updatedAtUtc,
        lastPublishedAtUtc: section.lastPublishedAtUtc ?? null
    };
    const serialised = stableStringify(payload);
    let hash = 0x811c9dc5;
    for (let index = 0; index < serialised.length; index += 1) {
        hash ^= serialised.charCodeAt(index);
        hash = Math.imul(hash, 0x01000193);
    }
    return `h:${(hash >>> 0).toString(16).padStart(8, "0")}`;
}
export function createSnapshotEnvelope(pageId, section, options = {}) {
    const parsedUpdatedAt = Date.parse(section.updatedAtUtc);
    const changeVersion = options.changeVersion ??
        (!Number.isNaN(parsedUpdatedAt) ? parsedUpdatedAt : Date.now());
    return {
        pageId,
        sectionId: section.id,
        selector: section.cssSelector ?? null,
        changeVersion,
        capturedAtUtc: options.capturedAtUtc ?? new Date().toISOString(),
        contentHash: hashPayload(section),
        etag: options.etag ?? section.etag ?? null,
        payload: section
    };
}
function detectAnnotations(changeType, before, after) {
    const annotations = [];
    if (changeType === "unchanged") {
        return annotations;
    }
    const beforeContent = before?.payload.contentValue ?? "";
    const afterContent = after?.payload.contentValue ?? "";
    if (changeType === "modified") {
        const delta = Math.abs((afterContent?.length ?? 0) - (beforeContent?.length ?? 0));
        if (delta > 1024) {
            annotations.push({
                code: "content.delta.large",
                message: "Section content changed by more than 1KB.",
                severity: "info",
                field: "contentValue"
            });
        }
        if (before?.payload.mediaPath !== after?.payload.mediaPath &&
            (before?.payload.mediaPath || after?.payload.mediaPath)) {
            annotations.push({
                code: "media.path.changed",
                message: "Primary media asset replaced.",
                severity: "warning",
                field: "mediaPath"
            });
        }
        if (before?.payload.mediaAltText !== after?.payload.mediaAltText &&
            after?.payload.mediaAltText === "") {
            annotations.push({
                code: "media.alt.missing",
                message: "Media alt text cleared during the update.",
                severity: "warning",
                field: "mediaAltText"
            });
        }
    }
    if (changeType === "added" && !afterContent) {
        annotations.push({
            code: "content.empty",
            message: "New section was introduced without content.",
            severity: "warning",
            field: "contentValue"
        });
    }
    if (changeType === "removed" && beforeContent) {
        annotations.push({
            code: "content.removal",
            message: "Section with content was removed.",
            severity: "warning",
            field: "contentValue"
        });
    }
    return annotations;
}
export function createSectionDiff(before, after, options = {}) {
    if (!before && !after) {
        return null;
    }
    if (!before && after) {
        return {
            pageId: after.pageId,
            sectionId: after.sectionId,
            changeType: "added",
            after,
            annotations: detectAnnotations("added", undefined, after)
        };
    }
    if (before && !after) {
        return {
            pageId: before.pageId,
            sectionId: before.sectionId,
            changeType: "removed",
            before,
            annotations: detectAnnotations("removed", before, undefined)
        };
    }
    if (!before || !after) {
        return null;
    }
    const changeType = before.contentHash === after.contentHash ? "unchanged" : "modified";
    const annotations = detectAnnotations(changeType, before, after);
    let conflict = false;
    if (options.detectConflicts &&
        changeType === "modified" &&
        typeof after.payload.previousContentValue === "string" &&
        after.payload.previousContentValue !== before.payload.contentValue) {
        conflict = true;
        annotations.push({
            code: "content.conflict",
            message: "Remote update differs from local baseline.",
            severity: "error",
            field: "contentValue"
        });
    }
    return {
        pageId: after.pageId,
        sectionId: after.sectionId,
        changeType,
        before,
        after,
        conflict,
        annotations
    };
}
export function diffSnapshotSets(before, after, options = {}) {
    const previousById = new Map();
    before.forEach((snapshot) => previousById.set(snapshot.sectionId, snapshot));
    const diffResults = [];
    after.forEach((snapshot) => {
        const prior = previousById.get(snapshot.sectionId) ?? null;
        previousById.delete(snapshot.sectionId);
        const diff = createSectionDiff(prior, snapshot, options);
        if (diff && diff.changeType !== "unchanged") {
            diffResults.push(diff);
        }
    });
    previousById.forEach((snapshot) => {
        const diff = createSectionDiff(snapshot, null, options);
        if (diff) {
            diffResults.push(diff);
        }
    });
    return diffResults;
}
