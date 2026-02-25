import {
  createEventBus,
  createStore,
  fetchHistory,
  fetchPageCollection
} from "@bugence/core";
import type {
  ContentHistoryEntry,
  ContentPageListItem,
  DiffEnvelope,
  IsoDateString,
  PageCollectionResponse,
  PublishSummary,
  ReviewStatus,
  SyncEventEnvelope,
  SyncEventPayloadMap,
  SyncEventTopic,
  SyncTelemetryPayload,
  Uuid
} from "@bugence/core";

const SYNC_CHANNEL_NAME = "bugence:sync";
const DEFAULT_SYNC_INTERVAL_MS = 15_000;
const TIMELINE_EVENT_LIMIT = 40;
const HISTORY_TRACK_LIMIT = 200;

type TimelineTone = "neutral" | "positive" | "warning" | "info";
type TimelineEventSource = "canvas" | "dashboard" | "history" | "workflow";
type TimelineEventType =
  | "draft-dirty"
  | "draft-synced"
  | "draft-committed"
  | "draft-conflict"
  | "review-status"
  | "publish-summary"
  | "history"
  | "sync-telemetry";

interface TimelineEvent {
  id: string;
  timestamp: IsoDateString;
  type: TimelineEventType;
  title: string;
  description?: string;
  tone: TimelineTone;
  source: TimelineEventSource;
  sectionId?: Uuid;
  sectionKey?: string;
  reviewStatus?: ReviewStatus;
}

interface DirtySectionState {
  sectionId: Uuid;
  sectionKey?: string;
  dirty: boolean;
  lastChangedAtUtc: IsoDateString;
  diff?: DiffEnvelope | null;
}

interface TimelineState {
  events: TimelineEvent[];
  dirtySections: Record<Uuid, DirtySectionState>;
  reviewStatuses: Record<Uuid, ReviewStatus>;
  conflictSectionIds: Uuid[];
  publishSummary?: PublishSummary | null;
  lastSyncedAtUtc?: IsoDateString | null;
  lastSyncTelemetry?: SyncTelemetryPayload | null;
  historyIds: string[];
}

export interface DashboardTimelineSnapshot {
  events: TimelineEvent[];
  dirtySections: Record<Uuid, DirtySectionState>;
  reviewStatuses: Record<Uuid, ReviewStatus>;
  conflictSectionIds: Uuid[];
  publishSummary?: PublishSummary | null;
  lastSyncedAtUtc?: IsoDateString | null;
  lastSyncTelemetry?: SyncTelemetryPayload | null;
}

type HistoryKey = string;

interface HistoryBucket {
  items: ContentHistoryEntry[];
  etag?: string | null;
  lastFetchedAtUtc?: string | null;
}

interface DashboardStoreState extends Record<string, unknown> {
  pages: ContentPageListItem[];
  totals: PageCollectionResponse["totals"] | null;
  pagesEtag?: string | null;
  selectedPageId?: string;
  isLoading: boolean;
  error?: string | null;
  lastFetchedAtUtc?: string | null;
  history: Record<HistoryKey, HistoryBucket>;
  isHistoryLoading: boolean;
  historyError?: string | null;
  timelineByPage: Record<Uuid, TimelineState>;
}

const initialState: DashboardStoreState = {
  pages: [],
  totals: null,
  pagesEtag: undefined,
  selectedPageId: undefined,
  isLoading: false,
  error: undefined,
  lastFetchedAtUtc: undefined,
  history: {},
  isHistoryLoading: false,
  historyError: undefined,
  timelineByPage: {}
};

const store = createStore<DashboardStoreState>(() => ({ ...initialState }));

export const dashboardStore = store;

const syncBus = createEventBus<SyncEventTopic, SyncEventPayloadMap[SyncEventTopic]>(
  SYNC_CHANNEL_NAME,
  {
    logger: (level, message, context) => {
      if (level === "error") {
        console.error("[dashboardSync]", message, context);
      }
    }
  }
);

let syncTimer: ReturnType<typeof setInterval> | null = null;

syncBus.subscribe(handleSyncEvent);

function createTimelineState(): TimelineState {
  return {
    events: [],
    dirtySections: {},
    reviewStatuses: {},
    conflictSectionIds: [],
    publishSummary: null,
    lastSyncedAtUtc: undefined,
    lastSyncTelemetry: undefined,
    historyIds: []
  };
}

function trimEvents(events: TimelineEvent[]): TimelineEvent[] {
  return events.slice(0, TIMELINE_EVENT_LIMIT);
}

function withTimelineState(pageId: string, updater: (current: TimelineState) => TimelineState): void {
  store.setState((current) => {
    const currentTimeline = current.timelineByPage[pageId] ?? createTimelineState();
    const nextTimeline = updater(currentTimeline);
    if (nextTimeline === currentTimeline && pageId in current.timelineByPage) {
      return current;
    }
    return {
      ...current,
      timelineByPage: {
        ...current.timelineByPage,
        [pageId]: nextTimeline
      }
    };
  });
}

function getTimelineStateSnapshot(pageId: string): TimelineState {
  return store.getState().timelineByPage[pageId] ?? createTimelineState();
}

function pushTimelineEvent(pageId: string, event: TimelineEvent) {
  withTimelineState(pageId, (current) => {
    const filtered = current.events.filter((entry) => entry.id !== event.id);
    return {
      ...current,
      events: trimEvents([event, ...filtered])
    };
  });
}

function setDirtySectionState(pageId: string, sectionId: string, nextState: DirtySectionState | null) {
  withTimelineState(pageId, (current) => {
    const next = { ...current.dirtySections };
    if (!nextState || !nextState.dirty) {
      if (!(sectionId in next)) {
        return current;
      }
      delete next[sectionId];
    } else {
      next[sectionId] = nextState;
    }
    return {
      ...current,
      dirtySections: next
    };
  });
}

function addConflictSection(pageId: string, sectionId: string) {
  withTimelineState(pageId, (current) => {
    if (current.conflictSectionIds.includes(sectionId)) {
      return current;
    }
    return {
      ...current,
      conflictSectionIds: [...current.conflictSectionIds, sectionId]
    };
  });
}

function clearConflictSection(pageId: string, sectionId: string) {
  withTimelineState(pageId, (current) => {
    if (!current.conflictSectionIds.includes(sectionId)) {
      return current;
    }
    return {
      ...current,
      conflictSectionIds: current.conflictSectionIds.filter((id) => id !== sectionId)
    };
  });
}

function setReviewStatus(pageId: string, sectionId: string, status: ReviewStatus | undefined) {
  withTimelineState(pageId, (current) => {
    const next = { ...current.reviewStatuses };
    if (!status) {
      if (!(sectionId in next)) {
        return current;
      }
      delete next[sectionId];
    } else {
      next[sectionId] = status;
    }
    return {
      ...current,
      reviewStatuses: next
    };
  });
}

function recordPublishSummary(pageId: string, summary: PublishSummary) {
  withTimelineState(pageId, (current) => ({
    ...current,
    publishSummary: summary
  }));
}

function recordSyncTelemetry(pageId: string, telemetry: SyncTelemetryPayload, syncedAtUtc?: IsoDateString) {
  withTimelineState(pageId, (current) => ({
    ...current,
    lastSyncTelemetry: telemetry,
    lastSyncedAtUtc:
      telemetry.result === "error"
        ? current.lastSyncedAtUtc
        : syncedAtUtc ?? new Date().toISOString()
  }));
}

function appendHistoryEntriesToTimeline(pageId: string, entries: ContentHistoryEntry[]) {
  if (!entries.length) {
    return;
  }

  withTimelineState(pageId, (current) => {
    const knownIds = new Set(current.historyIds);
    const newEvents: TimelineEvent[] = [];

    entries
      .slice()
      .sort((a, b) => Date.parse(b.performedAtUtc) - Date.parse(a.performedAtUtc))
      .forEach((entry) => {
        if (knownIds.has(entry.id)) {
          return;
        }
        knownIds.add(entry.id);
        newEvents.push(buildHistoryEvent(entry));
      });

    if (newEvents.length === 0) {
      return current;
    }

    return {
      ...current,
      events: trimEvents([...newEvents, ...current.events]),
      historyIds: Array.from(knownIds).slice(-HISTORY_TRACK_LIMIT)
    };
  });
}

function cleanSectionKey(diff: DiffEnvelope | null | undefined, fallback: string): string {
  return (
    diff?.after?.payload.sectionKey ??
    diff?.before?.payload.sectionKey ??
    fallback
  );
}

function resolveSectionLabel(pageId: string, sectionId: string, diff?: DiffEnvelope | null): string {
  const candidate = cleanSectionKey(diff, sectionId);
  if (candidate && candidate !== sectionId) {
    return candidate;
  }

  const timeline = store.getState().timelineByPage[pageId];
  if (timeline) {
    const dirty = timeline.dirtySections[sectionId];
    if (dirty?.sectionKey) {
      return dirty.sectionKey;
    }

    const summaryEntry = timeline.publishSummary?.entries.find(
      (entry) => entry.sectionId === sectionId
    );
    if (summaryEntry?.sectionKey) {
      return summaryEntry.sectionKey;
    }
  }

  return `Section ${sectionId.slice(0, 8)}`;
}

function describeDiff(diff?: DiffEnvelope | null): string | undefined {
  if (!diff) {
    return undefined;
  }

  const change = diff.changeType.charAt(0).toUpperCase() + diff.changeType.slice(1);
  const contentType =
    diff.after?.payload.contentType ?? diff.before?.payload.contentType;
  const key = cleanSectionKey(diff, diff.sectionId);
  const annotation = diff.annotations?.[0]?.message;

  const parts = [`${change} · ${key}`];
  if (contentType) {
    parts.push(contentType);
  }
  if (annotation) {
    parts.push(annotation);
  }

  return parts.join(" — ");
}

function describeReviewStatus(status: ReviewStatus): string {
  switch (status) {
    case "approved":
      return "Review approved";
    case "rejected":
      return "Review rejected";
    default:
      return "Review requested";
  }
}

function buildHistoryEvent(entry: ContentHistoryEntry): TimelineEvent {
  const actor = entry.performedByDisplayName ?? "System";
  const scope = entry.pageSectionId
    ? `Section ${entry.pageSectionId.slice(0, 8)}`
    : "Page";
  const diff = entry.diff ?? null;
  const deltaFragment = diff
    ? ` · Δ${diff.characterDelta >= 0 ? "+" : ""}${diff.characterDelta}`
    : "";
  const snippet = diff?.snippet?.trim();
  const snippetFragment = snippet ? ` · ${snippet}` : "";

  return {
    id: `history:${entry.id}`,
    timestamp: entry.performedAtUtc,
    type: "history",
    title: entry.changeSummary ?? `Updated ${entry.fieldKey}`,
    description: `${scope} · ${actor}${deltaFragment}${snippetFragment}`,
    tone: "neutral",
    source: "history",
    sectionId: entry.pageSectionId ?? undefined
  };
}

function handleSyncEvent(envelope: SyncEventEnvelope) {
  const { topic, payload, timestamp, id } = envelope;
  const pageId = (payload as { pageId?: string }).pageId;
  if (!pageId) {
    return;
  }

  switch (topic) {
    case "snapshot.applied": {
      const data = payload as SyncEventPayloadMap["snapshot.applied"];
      withTimelineState(pageId, (current) => ({
        ...current,
        lastSyncedAtUtc: data.appliedAtUtc
      }));
      if (data.source === "remote") {
        pushTimelineEvent(pageId, {
          id,
          timestamp,
          type: "history",
          title: "Remote snapshot applied",
          description: "Canvas changes pulled from server.",
          tone: "info",
          source: "canvas"
        });
      }
      break;
    }
    case "section.changed": {
      const data = payload as SyncEventPayloadMap["section.changed"];
      const sectionId = data.sectionId;
      const diff = data.diff;
      const sectionLabel = resolveSectionLabel(pageId, sectionId, diff);
      const previousDirty = Boolean(
        getTimelineStateSnapshot(pageId).dirtySections[sectionId]
      );

      if (data.dirty) {
        setDirtySectionState(pageId, sectionId, {
          sectionId,
          sectionKey: sectionLabel,
          dirty: true,
          lastChangedAtUtc: timestamp,
          diff
        });

        if (!previousDirty) {
          pushTimelineEvent(pageId, {
            id,
            timestamp,
            type: "draft-dirty",
            title: `Draft updated · ${sectionLabel}`,
            description: describeDiff(diff),
            tone: "info",
            source: "canvas",
            sectionId,
            sectionKey: sectionLabel
          });
        }
      } else {
        if (previousDirty) {
          pushTimelineEvent(pageId, {
            id: `${id}:clean`,
            timestamp,
            type: "draft-synced",
            title: `Draft aligned · ${sectionLabel}`,
            description: "Local changes match baseline.",
            tone: "positive",
            source: "canvas",
            sectionId,
            sectionKey: sectionLabel
          });
        }

        setDirtySectionState(pageId, sectionId, null);
      }
      break;
    }
    case "section.committed": {
      const data = payload as SyncEventPayloadMap["section.committed"];
      const sectionId = data.sectionId;
      const diff = data.diff;
      const sectionLabel = resolveSectionLabel(pageId, sectionId, diff);

      setDirtySectionState(pageId, sectionId, null);
      clearConflictSection(pageId, sectionId);

      pushTimelineEvent(pageId, {
        id,
        timestamp,
        type: "draft-committed",
        title: `Changes committed · ${sectionLabel}`,
        description: describeDiff(diff),
        tone: "positive",
        source: "canvas",
        sectionId,
        sectionKey: sectionLabel
      });
      break;
    }
    case "section.conflicted": {
      const data = payload as SyncEventPayloadMap["section.conflicted"];
      const sectionId = data.sectionId;
      const diff = data.diff;
      const sectionLabel = resolveSectionLabel(pageId, sectionId, diff);

      addConflictSection(pageId, sectionId);

      pushTimelineEvent(pageId, {
        id,
        timestamp,
        type: "draft-conflict",
        title: `Conflict detected · ${sectionLabel}`,
        description: describeDiff(diff) ?? "Resolve conflicts to continue.",
        tone: "warning",
        source: "canvas",
        sectionId,
        sectionKey: sectionLabel
      });
      break;
    }
    case "review.status.updated": {
      const data = payload as SyncEventPayloadMap["review.status.updated"];
      const sectionId = data.sectionId;
      const sectionLabel = resolveSectionLabel(pageId, sectionId);

      setReviewStatus(pageId, sectionId, data.status);

      pushTimelineEvent(pageId, {
        id,
        timestamp,
        type: "review-status",
        title: `${describeReviewStatus(data.status)} · ${sectionLabel}`,
        description: data.comment ?? undefined,
        tone:
          data.status === "approved"
            ? "positive"
            : data.status === "rejected"
            ? "warning"
            : "info",
        source: "workflow",
        sectionId,
        sectionKey: sectionLabel,
        reviewStatus: data.status
      });
      break;
    }
    case "timeline.publish.summary": {
      const data = payload as SyncEventPayloadMap["timeline.publish.summary"];
      recordPublishSummary(pageId, data.summary);
      pushTimelineEvent(pageId, {
        id,
        timestamp,
        type: "publish-summary",
        title: "Publish summary ready",
        description: `${data.summary.entries.length} section${data.summary.entries.length === 1 ? "" : "s"} staged for release.`,
        tone: "info",
        source: "workflow"
      });
      break;
    }
    case "sync.telemetry": {
      const data = payload as SyncEventPayloadMap["sync.telemetry"];
      const syncedAt = data.result === "error" ? undefined : timestamp;
      recordSyncTelemetry(pageId, data, syncedAt);

      if (data.result === "error") {
        pushTimelineEvent(pageId, {
          id,
          timestamp,
          type: "sync-telemetry",
          title: "Dashboard sync error",
          description: data.errorMessage ?? "Sync worker reported an error.",
          tone: "warning",
          source: data.source
        });
      }
      break;
    }
    default:
      break;
  }
}

function isBrowser(): boolean {
  return typeof window !== "undefined";
}

export function getDashboardState(): DashboardStoreState {
  return store.getState();
}

export function useDashboardStore() {
  return store;
}

export async function loadDashboardPages(options: {
  force?: boolean;
  ttlMs?: number;
} = {}): Promise<ContentPageListItem[]> {
  const { force = false, ttlMs } = options;
  const state = store.getState();

  if (state.isLoading) {
    return state.pages;
  }

  if (!force && state.pages.length > 0) {
    return state.pages;
  }

  store.setState((current) => ({
    ...current,
    isLoading: true,
    error: undefined
  }));

  try {
    const result = await fetchPageCollection({
      revalidate: force,
      ttlMs
    });

    const pages = result.data.pages ?? [];
    const totals = result.data.totals ?? null;
    const etag = result.etag ?? result.data.etag ?? state.pagesEtag;

    store.setState((current) => ({
      ...current,
      pages,
      totals,
      pagesEtag: etag ?? undefined,
      isLoading: false,
      lastFetchedAtUtc: new Date().toISOString(),
      error: undefined
    }));

    return pages;
  } catch (error) {
    const message =
      error instanceof Error ? error.message : "Unable to load pages.";

    store.setState((current) => ({
      ...current,
      isLoading: false,
      error: message
    }));

    throw error;
  }
}

export function selectDashboardPage(pageId?: string) {
  store.setState((current) => ({
    ...current,
    selectedPageId: pageId
  }));
  if (pageId) {
    withTimelineState(pageId, (currentState) => currentState);
  }
}

export function getSelectedDashboardPage(): ContentPageListItem | null {
  const state = store.getState();
  if (!state.selectedPageId) {
    return null;
  }

  return state.pages.find((page) => page.id === state.selectedPageId) ?? null;
}

export async function loadDashboardHistory(
  options: {
    pageId?: string;
    take?: number;
    force?: boolean;
    ttlMs?: number;
  } = {}
): Promise<ContentHistoryEntry[]> {
  const { pageId, take, force = false, ttlMs } = options;
  const key: HistoryKey = `${pageId ?? "all"}:${take ?? "default"}`;
  const state = store.getState();
  const cached = state.history[key];

  if (!force && cached && cached.items.length > 0) {
    return cached.items;
  }

  store.setState((current) => ({
    ...current,
    isHistoryLoading: true,
    historyError: undefined
  }));

  try {
    const result = await fetchHistory(
      { pageId, take },
      { revalidate: force, ttlMs }
    );

    const historyItems = result.data.history ?? [];
    const etag = result.etag ?? result.data.etag ?? cached?.etag;

    store.setState((current) => ({
      ...current,
      isHistoryLoading: false,
      history: {
        ...current.history,
        [key]: {
          items: historyItems,
          etag: etag ?? undefined,
          lastFetchedAtUtc: new Date().toISOString()
        }
      }
    }));

    if (pageId) {
      appendHistoryEntriesToTimeline(pageId, historyItems);
    }

    return historyItems;
  } catch (error) {
    const message =
      error instanceof Error ? error.message : "Unable to load history.";

    store.setState((current) => ({
      ...current,
      isHistoryLoading: false,
      historyError: message
    }));

    throw error;
  }
}

export function getDashboardHistory(
  pageId?: string,
  take?: number
): ContentHistoryEntry[] {
  const key: HistoryKey = `${pageId ?? "all"}:${take ?? "default"}`;
  return store.getState().history[key]?.items ?? [];
}

export function getTimelineSnapshot(pageId?: string): DashboardTimelineSnapshot {
  const resolvedPageId = pageId ?? store.getState().selectedPageId;
  if (!resolvedPageId) {
    return {
      events: [],
      dirtySections: {},
      reviewStatuses: {},
      conflictSectionIds: [],
      publishSummary: null,
      lastSyncedAtUtc: undefined,
      lastSyncTelemetry: undefined
    };
  }

  const timelineState = store.getState().timelineByPage[resolvedPageId];
  if (!timelineState) {
    return {
      events: [],
      dirtySections: {},
      reviewStatuses: {},
      conflictSectionIds: [],
      publishSummary: null,
      lastSyncedAtUtc: undefined,
      lastSyncTelemetry: undefined
    };
  }

  return {
    events: [...timelineState.events],
    dirtySections: { ...timelineState.dirtySections },
    reviewStatuses: { ...timelineState.reviewStatuses },
    conflictSectionIds: [...timelineState.conflictSectionIds],
    publishSummary: timelineState.publishSummary ?? null,
    lastSyncedAtUtc: timelineState.lastSyncedAtUtc,
    lastSyncTelemetry: timelineState.lastSyncTelemetry
  };
}

async function fetchHistoryForSync(pageId: string): Promise<ContentHistoryEntry[]> {
  const key: HistoryKey = `${pageId}:default`;
  const state = store.getState();
  const cached = state.history[key];

  const result = await fetchHistory(
    { pageId },
    { revalidate: true, ttlMs: 0 }
  );

  const historyItems = result.data.history ?? [];
  const etag = result.etag ?? result.data.etag ?? cached?.etag;

  store.setState((current) => ({
    ...current,
    history: {
      ...current.history,
      [key]: {
        items: historyItems,
        etag: etag ?? undefined,
        lastFetchedAtUtc: new Date().toISOString()
      }
    }
  }));

  return historyItems;
}

export function startDashboardSync(options: { pageId: string; intervalMs?: number }) {
  if (!isBrowser()) {
    return;
  }

  const { pageId, intervalMs } = options;
  const interval = Math.max(intervalMs ?? DEFAULT_SYNC_INTERVAL_MS, 5_000);

  if (syncTimer) {
    clearInterval(syncTimer);
    syncTimer = null;
  }

  const runSync = async () => {
    const startedAt = Date.now();
    try {
      const historyItems = await fetchHistoryForSync(pageId);
      appendHistoryEntriesToTimeline(pageId, historyItems);

      const durationMs = Date.now() - startedAt;
      const telemetry: SyncTelemetryPayload = {
        pageId,
        durationMs,
        result: "success",
        source: "dashboard"
      };

      recordSyncTelemetry(pageId, telemetry, new Date().toISOString());
      syncBus.publish("sync.telemetry", telemetry);
    } catch (error) {
      const durationMs = Date.now() - startedAt;
      const message = error instanceof Error ? error.message : String(error);
      const telemetry: SyncTelemetryPayload = {
        pageId,
        durationMs,
        result: "error",
        source: "dashboard",
        errorMessage: message
      };

      recordSyncTelemetry(pageId, telemetry);
      syncBus.publish("sync.telemetry", telemetry);
    }
  };

  void runSync();
  syncTimer = window.setInterval(runSync, interval);
}

export function stopDashboardSync() {
  if (syncTimer) {
    clearInterval(syncTimer);
    syncTimer = null;
  }
}
