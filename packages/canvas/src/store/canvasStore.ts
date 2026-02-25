import {
  createEventBus,
  createSectionDiff,
  createSnapshotEnvelope,
  createStore,
  diffSnapshotSets,
  fetchHistory,
  fetchSections,
  invalidateContentCache
} from "@bugence/core";
import type {
  CanvasError,
  CanvasSnapshot,
  CanvasStore,
  CanvasStoreState,
  ContentHistoryEntry,
  DiffEnvelope,
  PageSectionWithHistory,
  PublishSummary,
  ReviewStatus,
  ReviewStatusPayload,
  SectionsResponse,
  SectionMutationResponse,
  SectionCreatePayload,
  SectionUpdatePayload,
  SectionUpsertPayload,
  SnapshotEnvelope,
  SectionConflictPayload,
  SyncEventPayloadMap,
  SyncEventTopic,
  SyncTelemetryPayload
} from "@bugence/core";
import {
  clearBaseline,
  loadBaseline,
  saveBaseline
} from "./baselineCache";

const CONTENT_BASE = "/api/content";
const SYNC_CHANNEL_NAME = "bugence:sync";
const DEFAULT_SYNC_INTERVAL_MS = 15_000;

const syncBus = createEventBus<SyncEventTopic, SyncEventPayloadMap[SyncEventTopic]>(
  SYNC_CHANNEL_NAME,
  {
    logger: (level, message, context) => {
      if (level === "error") {
        console.error("[canvasSync]", message, context);
      }
    }
  }
);

let syncTimer: ReturnType<typeof setInterval> | null = null;

interface LoadOptions {
  force?: boolean;
  ttlMs?: number;
}

type SnapshotSource = "remote" | "local";

interface SnapshotDiff {
  added: PageSectionWithHistory[];
  updated: Array<{ previous: PageSectionWithHistory; next: PageSectionWithHistory }>;
  removed: PageSectionWithHistory[];
  pageChanged: boolean;
}

type CanvasEvent =
  | { type: "snapshot:applied"; snapshot: CanvasSnapshot; diff: SnapshotDiff; source: SnapshotSource; timestamp: string }
  | { type: "section:added"; section: PageSectionWithHistory; source: SnapshotSource }
  | { type: "section:updated"; previous: PageSectionWithHistory; next: PageSectionWithHistory; source: SnapshotSource }
  | { type: "section:removed"; section: PageSectionWithHistory; source: SnapshotSource }
  | { type: "section:optimistic"; sectionId: string }
  | { type: "section:committed"; section: PageSectionWithHistory }
  | { type: "section:conflict"; sectionId: string }
  | { type: "error"; error: CanvasError };

const canvasEventListeners = new Set<(event: CanvasEvent) => void>();

export function subscribeToCanvasEvents(listener: (event: CanvasEvent) => void): () => void {
  canvasEventListeners.add(listener);
  return () => canvasEventListeners.delete(listener);
}

function emitCanvasEvent(event: CanvasEvent) {
  canvasEventListeners.forEach((listener) => {
    try {
      listener(event);
    } catch (error) {
      console.error("[canvasStore] event listener error", error);
    }
  });
}

const initialState: CanvasStoreState = {
  page: null,
  sections: [],
  snapshot: null,
  baseline: {},
  dirtySectionIds: [],
  reviewStatuses: {},
  pageId: undefined,
  isLoading: false,
  isSaving: false,
  isPublishing: false,
  error: undefined,
  pageEtag: undefined,
  sectionsEtag: undefined,
  history: [],
  historyEtag: undefined,
  optimisticSectionIds: [],
  conflictSectionIds: [],
  lastFetchedAtUtc: undefined,
  lastHistoryFetchedAtUtc: undefined,
  lastSyncedAtUtc: undefined
};

const store = createStore<CanvasStoreState>(() => ({ ...initialState }));

syncBus.subscribe((event) => {
  const state = store.getState();
  if (!state.pageId || event.payload.pageId !== state.pageId) {
    return;
  }

  switch (event.topic) {
    case "review.status.updated": {
      const payload = event.payload as ReviewStatusPayload;
      store.setState((current) => ({
        ...current,
        reviewStatuses: {
          ...current.reviewStatuses,
          [payload.sectionId]: payload.status
        }
      }));
      break;
    }
    case "section.conflicted": {
      const payload = event.payload as SectionConflictPayload;
      store.setState((current) => ({
        ...current,
        conflictSectionIds: Array.from(
          new Set([...current.conflictSectionIds, payload.sectionId])
        )
      }));
      break;
    }
    default:
      break;
  }
});

export const canvasStore: CanvasStore = {
  getState: store.getState,
  subscribe: store.subscribe,
  setState: store.setState,
  reset: () => {
    const pageId = store.getState().pageId;
    if (pageId) {
      clearBaseline(pageId);
    } else {
      clearBaseline();
    }
    stopSyncWorker();
    store.reset();
  }
};

export function getCanvasState(): CanvasStoreState {
  return store.getState();
}

export async function loadCanvas(
  pageId: string,
  options: LoadOptions = {}
): Promise<CanvasStoreState> {
  await loadCanvasSections(pageId, options);
  await loadCanvasHistory(pageId, options);
  return store.getState();
}

export async function loadCanvasSections(
  pageId: string,
  options: LoadOptions = {}
): Promise<PageSectionWithHistory[]> {
  const state = store.getState();
  if (state.isLoading && !options.force) {
    return state.sections;
  }

  store.setState((current) => ({
    ...current,
    isLoading: true,
    error: undefined,
    pageId
  }));

  try {
    const result = await fetchSections(pageId, {
      revalidate: options.force,
      ttlMs: options.ttlMs
    });

    const data: SectionsResponse = result.data;
    const sections = normaliseSections(data.sections);
    const page = data.page;
    const sectionsEtag = result.etag ?? data.etag ?? store.getState().sectionsEtag;
    const pageEtag = data.pageEtag ?? store.getState().pageEtag;

    const snapshot: CanvasSnapshot = {
      page,
      sections,
      retrievedAt: new Date().toISOString()
    };

    commitSnapshot(
      snapshot,
      "remote",
      (current) => ({
        isLoading: false,
        pageId,
        sectionsEtag: sectionsEtag ?? current.sectionsEtag,
        pageEtag: pageEtag ?? current.pageEtag,
        lastFetchedAtUtc: snapshot.retrievedAt,
        lastSyncedAtUtc: snapshot.retrievedAt,
        optimisticSectionIds: [],
        conflictSectionIds: current.conflictSectionIds.filter((id) =>
          sections.some((section) => section.id === id)
        ),
        error: undefined
      }),
      { resetBaseline: options.force ?? false }
    );

    ensureSyncWorker(pageId, DEFAULT_SYNC_INTERVAL_MS);

    return sections;
  } catch (error) {
    const message =
      error instanceof Error ? error.message : "Unable to load sections.";

    store.setState((current) => ({
      ...current,
      isLoading: false,
      error: {
        message
      }
    }));

    emitCanvasEvent({
      type: "error",
      error: {
        message
      }
    });

    throw error;
  }
}

export async function loadCanvasHistory(
  pageId: string,
  options: LoadOptions = {}
): Promise<ContentHistoryEntry[]> {
  const state = store.getState();
  if (!options.force && state.history.length > 0) {
    return state.history;
  }

  try {
    const result = await fetchHistory(
      { pageId },
      {
        revalidate: options.force,
        ttlMs: options.ttlMs
      }
    );

    const history = result.data.history ?? [];
    const etag = result.etag ?? result.data.etag ?? store.getState().historyEtag;

    store.setState((current) => ({
      ...current,
      history,
      historyEtag: etag ?? undefined,
      lastHistoryFetchedAtUtc: new Date().toISOString()
    }));

    return history;
  } catch (error) {
    const message =
      error instanceof Error ? error.message : "Unable to load history.";
    store.setState((current) => ({
      ...current,
      historyEtag: current.historyEtag,
      error: {
        message
      }
    }));
    return store.getState().history;
  }
}

export async function createCanvasSection(
  pageId: string,
  payload: SectionCreatePayload
): Promise<PageSectionWithHistory> {
  const selector = payload.selector?.trim();
  if (!selector) {
    throw new Error("Selector is required to create a section.");
  }

  const stateBefore = store.getState();
  if (!stateBefore.page) {
    throw new Error("Canvas page must be loaded before creating sections.");
  }

  const formPayload: SectionUpsertPayload = {
    ...payload,
    selector,
    contentType: payload.contentType ?? "RichText"
  };

  store.setState((current) => ({
    ...current,
    isSaving: true,
    error: undefined
  }));

  const response = await fetch(`${CONTENT_BASE}/pages/${pageId}/sections`, {
    method: "POST",
    body: createSectionFormData(formPayload)
  });

  if (!response.ok) {
    const message = await response.text();
    store.setState((current) => ({
      ...current,
      isSaving: false,
      error: {
        message: message || "Unable to create section."
      }
    }));
    throw new Error(message || "Unable to create section.");
  }

  const json = (await response.json()) as SectionMutationResponse;
  if (!json.section) {
    store.setState((current) => ({
      ...current,
      isSaving: false
    }));
    throw new Error("Server did not return the created section.");
  }

  const created = normaliseSection(json.section);
  const pageEtag =
    response.headers.get("X-Page-ETag") ??
    json.pageEtag ??
    store.getState().pageEtag;

  const snapshot = captureSnapshotFromState();
  if (snapshot) {
    const nextSnapshot = upsertSectionInSnapshot(snapshot, created);
    commitSnapshot(nextSnapshot, "local", (current) => ({
      isSaving: false,
      error: undefined,
      pageEtag: pageEtag ?? current.pageEtag
    }));
  } else {
    store.setState((current) => ({
      ...current,
      sections: [...current.sections, created],
      dirtySectionIds: Array.from(new Set([...(current.dirtySectionIds ?? []), created.id])),
      isSaving: false,
      error: undefined,
      pageEtag: pageEtag ?? undefined
    }));
  }

  const diffEnvelope = createSectionDiff(
    null,
    createSnapshotEnvelope(pageId, created, { etag: created.etag ?? null }),
    { detectConflicts: true }
  );

  syncBus.publish("section.changed", {
    pageId,
    sectionId: created.id,
    diff: diffEnvelope ?? null,
    dirty: true
  });

  invalidateContentCache(pageId);

  return created;
}

export async function duplicateCanvasSection(
  pageId: string,
  sectionId: string,
  options: { variantLabel?: string } = {}
): Promise<PageSectionWithHistory> {
  const state = store.getState();
  const original = state.sections.find((section) => section.id === sectionId);
  if (!original) {
    throw new Error("Section not found.");
  }
  if (!original.cssSelector) {
    throw new Error("Section cannot be duplicated without a selector.");
  }
  if (original.contentType === "Image") {
    throw new Error("Image sections cannot be duplicated automatically.");
  }

  const contentValue =
    typeof original.contentValue === "string"
      ? buildDuplicateContent(original.contentValue, options.variantLabel)
      : original.contentValue;

  return createCanvasSection(pageId, {
    selector: original.cssSelector,
    contentType: original.contentType,
    contentValue,
    mediaAltText: original.mediaAltText ?? undefined
  });
}

export async function saveCanvasSection(
  pageId: string,
  payload: SectionUpdatePayload
): Promise<PageSectionWithHistory> {
  const state = store.getState();
  const target = state.sections.find((section) => section.id === payload.sectionId);
  if (!target) {
    throw new Error("Section not found.");
  }

  const baseSnapshot = captureSnapshotFromState();
  const original = { ...target };

  const optimistic: PageSectionWithHistory = {
    ...target,
    contentValue:
      payload.contentValue !== undefined ? payload.contentValue : target.contentValue,
    mediaAltText:
      payload.mediaAltText !== undefined ? payload.mediaAltText : target.mediaAltText,
    updatedAtUtc: new Date().toISOString(),
    etag: target.etag
  };

  if (baseSnapshot) {
    const optimisticSnapshot = upsertSectionInSnapshot(baseSnapshot, optimistic);
    commitSnapshot(optimisticSnapshot, "local", (current) => ({
      optimisticSectionIds: Array.from(new Set([...current.optimisticSectionIds, optimistic.id])),
      conflictSectionIds: current.conflictSectionIds.filter((id) => id !== optimistic.id),
      isSaving: true,
      error: undefined
    }));
    emitCanvasEvent({ type: "section:optimistic", sectionId: optimistic.id });
  } else {
    store.setState((current) => ({
      ...current,
      sections: current.sections.map((section) =>
        section.id === optimistic.id ? optimistic : section
      ),
      optimisticSectionIds: Array.from(new Set([...current.optimisticSectionIds, optimistic.id])),
      conflictSectionIds: current.conflictSectionIds.filter((id) => id !== optimistic.id),
      isSaving: true,
      error: undefined
    }));
    emitCanvasEvent({ type: "section:optimistic", sectionId: optimistic.id });
  }

  const formData = createSectionFormData(payload);
  const headers: HeadersInit = {};
  if (target.etag) {
    headers["If-Match"] = target.etag;
  }

  const response = await fetch(`${CONTENT_BASE}/pages/${pageId}/sections`, {
    method: "POST",
    body: formData,
    headers
  });

  if (response.status === 412) {
    handleSectionConflict(pageId, original);
    throw new Error("Section conflict");
  }

  if (!response.ok) {
    await revertSectionUpdate(original, response);
    throw new Error("Unable to save section.");
  }

  const json = (await response.json()) as SectionMutationResponse;
  const updated = normaliseSection(json.section ?? optimistic);
  const sectionEtag =
    response.headers.get("ETag") ??
    json.section?.etag ??
    store.getState().sections.find((section) => section.id === updated.id)?.etag ??
    undefined;

  updated.etag = sectionEtag ?? undefined;

  const pageEtag =
    response.headers.get("X-Page-ETag") ??
    json.pageEtag ??
    store.getState().pageEtag;

  const currentSnapshot = captureSnapshotFromState();
  if (currentSnapshot) {
    const committedSnapshot = upsertSectionInSnapshot(currentSnapshot, updated);
    commitSnapshot(committedSnapshot, "local", (current) => ({
      optimisticSectionIds: current.optimisticSectionIds.filter((id) => id !== updated.id),
      isSaving: false,
      pageEtag: pageEtag ?? current.pageEtag,
      error: undefined
    }));
    emitCanvasEvent({ type: "section:committed", section: updated });
  } else {
    store.setState((current) => ({
      ...current,
      sections: current.sections.map((section) =>
        section.id === updated.id ? updated : section
      ),
      optimisticSectionIds: current.optimisticSectionIds.filter((id) => id !== updated.id),
      isSaving: false,
      pageEtag: pageEtag ?? undefined,
      error: undefined
    }));
    emitCanvasEvent({ type: "section:committed", section: updated });
  }

  const baselineSnapshot =
    store.getState().baseline[updated.id] ?? null;
  const diffEnvelope = createSectionDiff(
    baselineSnapshot ?? null,
    createSnapshotEnvelope(pageId, updated, { etag: updated.etag ?? null }),
    { detectConflicts: true }
  );

  syncBus.publish("section.committed", {
    pageId,
    sectionId: updated.id,
    diff: diffEnvelope ?? null,
    committedAtUtc: new Date().toISOString()
  });

  invalidateContentCache(pageId);
  return updated;
}

export async function deleteCanvasSection(
  pageId: string,
  sectionId: string
): Promise<void> {
  const state = store.getState();
  const target = state.sections.find((section) => section.id === sectionId);
  if (!target) {
    return;
  }

  const headers: HeadersInit = {};
  if (target.etag) {
    headers["If-Match"] = target.etag;
  }

  const response = await fetch(
    `${CONTENT_BASE}/pages/${pageId}/sections/${sectionId}`,
    {
      method: "DELETE",
      headers
    }
  );

  if (response.status === 412) {
    handleSectionConflict(pageId, target);
    throw new Error("Section conflict");
  }

  if (!response.ok) {
    const message = await response.text();
    throw new Error(message || "Unable to delete section.");
  }

  const json = await response.json();
  const remaining = store.getState().sections.filter((section) => section.id !== sectionId);
  const history = store.getState().history.filter((entry) => entry.pageSectionId !== sectionId);

  const pageEtag =
    response.headers.get("ETag") ??
    response.headers.get("X-Page-ETag") ??
    json?.pageEtag ??
    store.getState().pageEtag;

  const snapshot = captureSnapshotFromState();
  if (snapshot) {
    const nextSnapshot = removeSectionFromSnapshot(snapshot, sectionId);
    commitSnapshot(nextSnapshot, "local", (current) => {
      const nextReviewStatuses = { ...current.reviewStatuses };
      delete nextReviewStatuses[sectionId];
      return {
        history,
        pageEtag: pageEtag ?? current.pageEtag,
        sectionsEtag: current.sectionsEtag,
        optimisticSectionIds: current.optimisticSectionIds.filter((id) => id !== sectionId),
        conflictSectionIds: current.conflictSectionIds.filter((id) => id !== sectionId),
        reviewStatuses: nextReviewStatuses
      };
    });
  } else {
    store.setState((current) => {
      const nextReviewStatuses = { ...current.reviewStatuses };
      delete nextReviewStatuses[sectionId];
      return {
        ...current,
        sections: remaining,
        history,
        pageEtag: pageEtag ?? undefined,
        sectionsEtag: current.sectionsEtag,
        optimisticSectionIds: current.optimisticSectionIds.filter((id) => id !== sectionId),
        conflictSectionIds: current.conflictSectionIds.filter((id) => id !== sectionId),
        reviewStatuses: nextReviewStatuses
      };
    });
  }

  const baselineSnapshot = store.getState().baseline[sectionId] ?? null;
  const removalDiff = createSectionDiff(
    baselineSnapshot ?? null,
    null,
    { detectConflicts: true }
  );

  syncBus.publish("section.committed", {
    pageId,
    sectionId,
    diff: removalDiff ?? null,
    committedAtUtc: new Date().toISOString()
  });

  invalidateContentCache(pageId);
}

export async function publishCanvasPage(pageId: string): Promise<void> {
  const state = store.getState();
  if (state.isPublishing) {
    return;
  }

  const headers: HeadersInit = {};
  if (state.pageEtag) {
    headers["If-Match"] = state.pageEtag;
  }

  store.setState((current) => ({
    ...current,
    isPublishing: true,
    error: undefined
  }));

  const response = await fetch(
    `${CONTENT_BASE}/pages/${pageId}/publish`,
    {
      method: "POST",
      headers
    }
  );

  if (response.status === 412) {
    store.setState((current) => ({
      ...current,
      isPublishing: false,
      error: {
        message: "Page has been updated elsewhere. Reload before publishing.",
        status: 412
      }
    }));
    await loadCanvas(pageId, { force: true });
    throw new Error("Publish conflict");
  }

  if (!response.ok) {
    const message = await response.text();
    store.setState((current) => ({
      ...current,
      isPublishing: false,
      error: {
        message: message || "Unable to publish page."
      }
    }));
    throw new Error(message || "Unable to publish page.");
  }

  const json = await response.json();
  const pageEtag =
    response.headers.get("ETag") ??
    response.headers.get("X-Page-ETag") ??
    json?.etag ??
    store.getState().pageEtag;

  const stateAfterPublish = store.getState();
  const publishDiffs = diffSnapshotSets(
    Object.values(stateAfterPublish.baseline),
    buildSnapshotEnvelopes(pageId, stateAfterPublish.sections),
    { detectConflicts: true }
  );
  const summary = createPublishSummary(pageId, publishDiffs, stateAfterPublish.reviewStatuses);
  const baselineSnapshots = buildSnapshotEnvelopes(pageId, stateAfterPublish.sections);

  saveBaseline(pageId, baselineSnapshots, Date.now());

  store.setState((current) => ({
    ...current,
    isPublishing: false,
    pageEtag: pageEtag ?? undefined,
    error: undefined,
    baseline: snapshotArrayToMap(baselineSnapshots),
    dirtySectionIds: [],
    reviewStatuses: {},
    lastSyncedAtUtc: new Date().toISOString()
  }));

  if (summary.entries.length > 0) {
    syncBus.publish("timeline.publish.summary", {
      pageId,
      summary,
      preparedAtUtc: summary.generatedAtUtc
    });
  }

  invalidateContentCache(pageId);
}

function normaliseSections(
  sections: PageSectionWithHistory[] | undefined
): PageSectionWithHistory[] {
  if (!sections?.length) {
    return [];
  }

  return sections.map(normaliseSection);
}

function normaliseSection(section: PageSectionWithHistory): PageSectionWithHistory {
  return {
    ...section,
    updatedAtUtc: section.updatedAtUtc ?? new Date().toISOString()
  };
}

function buildDuplicateContent(
  value: string | null | undefined,
  label?: string
): string | null | undefined {
  if (value === null || value === undefined) {
    return value;
  }

  const suffix = label ? ` (Variant ${label})` : " (Copy)";
  const containsHtml = /<\w/i.test(value);

  if (containsHtml) {
    if (value.includes("<!-- duplicate")) {
      return value;
    }
    return `${value}\n<!-- duplicate ${suffix.trim()} -->`;
  }

  const base = value.endsWith(" ") ? value.trimEnd() : value;
  if (base.endsWith(suffix)) {
    return base;
  }

  return `${base}${suffix}`;
}

function createSectionFormData(payload: SectionUpsertPayload): FormData {
  const formData = new FormData();

  if (payload.sectionId) {
    formData.set("SectionId", payload.sectionId);
  }

  if (payload.selector) {
    formData.set("Selector", payload.selector);
  }

  if (payload.contentType) {
    formData.set("ContentType", payload.contentType);
  }

  if (payload.contentValue !== undefined) {
    formData.set("ContentValue", payload.contentValue ?? "");
  }

  if (payload.mediaAltText !== undefined) {
    formData.set("MediaAltText", payload.mediaAltText ?? "");
  }

  if (payload.image) {
    formData.set("Image", payload.image);
  }

  return formData;
}

function captureSnapshotFromState(): CanvasSnapshot | null {
  const state = store.getState();
  if (!state.page) {
    return null;
  }

  return {
    page: state.page,
    sections: state.sections,
    retrievedAt: new Date().toISOString()
  };
}

function hasSectionChanged(previous: PageSectionWithHistory, next: PageSectionWithHistory): boolean {
  if (previous.updatedAtUtc !== next.updatedAtUtc) {
    return true;
  }

  return (
    previous.contentValue !== next.contentValue ||
    previous.mediaPath !== next.mediaPath ||
    previous.mediaAltText !== next.mediaAltText ||
    previous.displayOrder !== next.displayOrder ||
    previous.isLocked !== next.isLocked
  );
}

function hasPageChanged(previous: CanvasSnapshot["page"], next: CanvasSnapshot["page"]): boolean {
  return (
    previous.updatedAtUtc !== next.updatedAtUtc ||
    previous.name !== next.name ||
    previous.description !== next.description ||
    previous.slug !== next.slug
  );
}

function diffSnapshots(previous: CanvasSnapshot | null, next: CanvasSnapshot): SnapshotDiff {
  if (!previous) {
    return {
      added: [...next.sections],
      updated: [],
      removed: [],
      pageChanged: true
    };
  }

  const previousMap = new Map(previous.sections.map((section) => [section.id, section]));
  const added: PageSectionWithHistory[] = [];
  const updated: Array<{ previous: PageSectionWithHistory; next: PageSectionWithHistory }> = [];

  next.sections.forEach((section) => {
    const prior = previousMap.get(section.id);
    if (!prior) {
      added.push(section);
      return;
    }

    if (hasSectionChanged(prior, section)) {
      updated.push({ previous: prior, next: section });
    }

    previousMap.delete(section.id);
  });

  const removed = Array.from(previousMap.values());

  return {
    added,
    updated,
    removed,
    pageChanged: hasPageChanged(previous.page, next.page)
  };
}

function commitSnapshot(
  snapshot: CanvasSnapshot,
  source: SnapshotSource,
  partial?: (current: CanvasStoreState) => Partial<CanvasStoreState>,
  options: { resetBaseline?: boolean } = {}
): SnapshotDiff {
  const previousState = store.getState();
  const previousSnapshot = previousState.snapshot;
  const diff = diffSnapshots(previousSnapshot, snapshot);
  const timestamp = new Date().toISOString();
  const pageId = snapshot.page.id;

  const baselineBefore = resolveBaselineMap(pageId, previousState.baseline);
  const shouldResetBaseline =
    options.resetBaseline ??
    (source === "remote" && Object.keys(baselineBefore).length === 0);

  let baselineAfter: Record<string, SnapshotEnvelope>;
  let persistedSnapshots: SnapshotEnvelope[] | null = null;

  if (shouldResetBaseline) {
    persistedSnapshots = buildSnapshotEnvelopes(pageId, snapshot.sections);
    baselineAfter = snapshotArrayToMap(persistedSnapshots);
    saveBaseline(pageId, persistedSnapshots, Date.now());
  } else {
    baselineAfter = { ...baselineBefore };
  }

  const dirtySectionIds = computeDirtySectionIds(pageId, snapshot.sections, baselineAfter);

  store.setState((current) => {
    const partialUpdate = partial ? partial(current) : {};
    const optimisticIds =
      partialUpdate.optimisticSectionIds ?? current.optimisticSectionIds ?? [];
    const conflictIds =
      partialUpdate.conflictSectionIds ?? current.conflictSectionIds ?? [];
    const dirtySet = new Set<string>([
      ...dirtySectionIds,
      ...optimisticIds,
      ...conflictIds
    ]);

    return {
      ...current,
      ...partialUpdate,
      page: snapshot.page,
      sections: snapshot.sections,
      snapshot,
      baseline: baselineAfter,
      dirtySectionIds: Array.from(dirtySet)
    };
  });

  emitCanvasEvent({
    type: "snapshot:applied",
    snapshot,
    diff,
    source,
    timestamp
  });

  diff.added.forEach((section) =>
    emitCanvasEvent({ type: "section:added", section, source })
  );

  diff.updated.forEach(({ previous, next }) =>
    emitCanvasEvent({ type: "section:updated", previous, next, source })
  );

  diff.removed.forEach((section) =>
    emitCanvasEvent({ type: "section:removed", section, source })
  );

  const currentSnapshots = buildSnapshotEnvelopes(pageId, snapshot.sections);

  syncBus.publish("snapshot.applied", {
    pageId,
    snapshot: currentSnapshots,
    source,
    appliedAtUtc: timestamp
  });

  const diffEnvelopes = diffSnapshotSets(
    Object.values(baselineBefore),
    currentSnapshots,
    { detectConflicts: true }
  );

  const currentDirty = store.getState().dirtySectionIds;
  diffEnvelopes.forEach((envelope) => {
    if (envelope.changeType === "unchanged") {
      return;
    }
    const dirty = currentDirty.includes(envelope.sectionId);
    syncBus.publish("section.changed", {
      pageId,
      sectionId: envelope.sectionId,
      diff: envelope,
      dirty
    });
  });

  return diff;
}

function upsertSectionInSnapshot(
  snapshot: CanvasSnapshot,
  section: PageSectionWithHistory
): CanvasSnapshot {
  const sections = snapshot.sections.some((entry) => entry.id === section.id)
    ? snapshot.sections.map((entry) => (entry.id === section.id ? section : entry))
    : [...snapshot.sections, section];

  return {
    ...snapshot,
    sections,
    retrievedAt: new Date().toISOString()
  };
}

function removeSectionFromSnapshot(
  snapshot: CanvasSnapshot,
  sectionId: string
): CanvasSnapshot {
  return {
    ...snapshot,
    sections: snapshot.sections.filter((section) => section.id !== sectionId),
    retrievedAt: new Date().toISOString()
  };
}

function resolveBaselineMap(
  pageId: string,
  existing: Record<string, SnapshotEnvelope> | undefined
): Record<string, SnapshotEnvelope> {
  const baseline = existing ?? {};
  if (Object.keys(baseline).length > 0) {
    return baseline;
  }

  const persisted = loadBaseline(pageId);
  if (!persisted) {
    return baseline;
  }

  return snapshotArrayToMap(persisted.snapshots);
}

function buildSnapshotEnvelopes(
  pageId: string,
  sections: PageSectionWithHistory[]
): SnapshotEnvelope[] {
  return sections.map((section) =>
    createSnapshotEnvelope(pageId, section, {
      etag: section.etag ?? null
    })
  );
}

function snapshotArrayToMap(
  snapshots: SnapshotEnvelope[]
): Record<string, SnapshotEnvelope> {
  const map: Record<string, SnapshotEnvelope> = {};
  snapshots.forEach((snapshot) => {
    map[snapshot.sectionId] = snapshot;
  });
  return map;
}

function computeDirtySectionIds(
  pageId: string,
  sections: PageSectionWithHistory[],
  baseline: Record<string, SnapshotEnvelope>
): string[] {
  if (!sections.length) {
    return [];
  }

  const snapshotDiffs = diffSnapshotSets(
    Object.values(baseline),
    buildSnapshotEnvelopes(pageId, sections),
    { detectConflicts: true }
  );

  const currentIds = new Set(sections.map((section) => section.id));

  return snapshotDiffs
    .filter((diff) => diff.changeType !== "unchanged" && currentIds.has(diff.sectionId))
    .map((diff) => diff.sectionId);
}

function ensureSyncWorker(pageId: string, intervalMs: number) {
  if (typeof window === "undefined") {
    return;
  }

  if (syncTimer) {
    stopSyncWorker();
  }

  syncTimer = window.setInterval(() => {
    void runBackgroundSync(pageId);
  }, Math.max(intervalMs, 5_000));
}

function stopSyncWorker() {
  if (syncTimer) {
    clearInterval(syncTimer);
    syncTimer = null;
  }
}

async function runBackgroundSync(pageId: string) {
  if (!pageId) {
    return;
  }

  const state = store.getState();
  if (state.isSaving || state.isPublishing) {
    return;
  }

  const headers: HeadersInit = {};
  if (state.sectionsEtag) {
    headers["If-None-Match"] = state.sectionsEtag;
  }

  const startedAt = Date.now();

  try {
    const response = await fetch(`${CONTENT_BASE}/pages/${pageId}/sections`, {
      headers
    });

    if (response.status === 304) {
      const syncedAt = new Date().toISOString();
      store.setState((current) => ({
        ...current,
        lastSyncedAtUtc: syncedAt
      }));
      publishSyncTelemetry({
        pageId,
        durationMs: Date.now() - startedAt,
        result: "noop",
        source: "canvas"
      });
      return;
    }

    if (!response.ok) {
      throw new Error(response.statusText || "Sync failed");
    }

    const data: SectionsResponse = await response.json();
    const sections = normaliseSections(data.sections);
    const page = data.page;

    const snapshot: CanvasSnapshot = {
      page,
      sections,
      retrievedAt: new Date().toISOString()
    };

    const sectionsEtag = response.headers.get("ETag") ?? data.etag ?? state.sectionsEtag;
    const pageEtag =
      response.headers.get("X-Page-ETag") ?? data.pageEtag ?? state.pageEtag;

    commitSnapshot(snapshot, "remote", (current) => ({
      sectionsEtag: sectionsEtag ?? current.sectionsEtag,
      pageEtag: pageEtag ?? current.pageEtag,
      lastSyncedAtUtc: snapshot.retrievedAt
    }));

    publishSyncTelemetry({
      pageId,
      durationMs: Date.now() - startedAt,
      result: "success",
      source: "canvas"
    });
  } catch (error) {
    publishSyncTelemetry({
      pageId,
      durationMs: Date.now() - startedAt,
      result: "error",
      source: "canvas",
      errorMessage: error instanceof Error ? error.message : String(error)
    });
  }
}

function publishSyncTelemetry(payload: SyncTelemetryPayload) {
  syncBus.publish("sync.telemetry", payload);
}

function createPublishSummary(
  pageId: string,
  diffs: DiffEnvelope[],
  reviewStatuses: Record<string, ReviewStatus>
): PublishSummary {
  const generatedAtUtc = new Date().toISOString();
  const entries = diffs
    .filter((diff) => diff.changeType !== "unchanged")
    .map((diff) => {
      const payload = diff.after?.payload ?? diff.before?.payload;
      const reviewerStatus = reviewStatuses[diff.sectionId];
      return {
        sectionId: diff.sectionId,
        sectionKey: payload?.sectionKey ?? "unknown",
        contentType: payload?.contentType ?? "Text",
        changeType: diff.changeType,
        reviewerStatus,
        reviewerComment: null,
        diffId:
          diff.after?.contentHash ??
          diff.before?.contentHash ??
          `${diff.sectionId}:${generatedAtUtc}`
      };
    });

  return {
    pageId,
    generatedAtUtc,
    entries,
    notes: null
  };
}

async function revertSectionUpdate(
  original: PageSectionWithHistory,
  response: Response
) {
  const message = await response.text();
  const error: CanvasError = {
    message: message || "Unable to save section.",
    status: response.status,
    sectionId: original.id
  };

  const snapshot = captureSnapshotFromState();
  if (snapshot) {
    const revertedSnapshot = upsertSectionInSnapshot(snapshot, original);
    commitSnapshot(revertedSnapshot, "local", (current) => ({
      optimisticSectionIds: current.optimisticSectionIds.filter((id) => id !== original.id),
      isSaving: false,
      error
    }));
  } else {
    store.setState((current) => ({
      ...current,
      sections: current.sections.map((section) =>
        section.id === original.id ? original : section
      ),
      optimisticSectionIds: current.optimisticSectionIds.filter((id) => id !== original.id),
      isSaving: false,
      error
    }));
  }

  emitCanvasEvent({ type: "error", error });
}

function handleSectionConflict(pageId: string, original: PageSectionWithHistory) {
  const snapshot = captureSnapshotFromState();
  const error: CanvasError = {
    message: "Section has been updated by another editor.",
    status: 412,
    sectionId: original.id
  };

  if (snapshot) {
    const revertedSnapshot = upsertSectionInSnapshot(snapshot, original);
    commitSnapshot(revertedSnapshot, "local", (current) => ({
      optimisticSectionIds: current.optimisticSectionIds.filter((id) => id !== original.id),
      conflictSectionIds: Array.from(new Set([...current.conflictSectionIds, original.id])),
      isSaving: false,
      error
    }));
  } else {
    store.setState((current) => ({
      ...current,
      sections: current.sections.map((section) =>
        section.id === original.id ? original : section
      ),
      optimisticSectionIds: current.optimisticSectionIds.filter((id) => id !== original.id),
      conflictSectionIds: Array.from(new Set([...current.conflictSectionIds, original.id])),
      isSaving: false,
      error
    }));
  }

  emitCanvasEvent({ type: "section:conflict", sectionId: original.id });

  const baselineSnapshot =
    store.getState().baseline[original.id] ?? null;
  const conflictDiff = createSectionDiff(
    baselineSnapshot ?? null,
    createSnapshotEnvelope(pageId, original, { etag: original.etag ?? null }),
    { detectConflicts: true }
  );

  syncBus.publish("section.conflicted", {
    pageId,
    sectionId: original.id,
    diff: conflictDiff ?? null,
    detectedAtUtc: new Date().toISOString()
  });

  void loadCanvasSections(pageId, { force: true });
}
