import type { ContentHistoryEntry, ContentPageListItem, DiffEnvelope, IsoDateString, PageCollectionResponse, PublishSummary, ReviewStatus, SyncTelemetryPayload, Uuid } from "@bugence/core";
type TimelineTone = "neutral" | "positive" | "warning" | "info";
type TimelineEventSource = "canvas" | "dashboard" | "history" | "workflow";
type TimelineEventType = "draft-dirty" | "draft-synced" | "draft-committed" | "draft-conflict" | "review-status" | "publish-summary" | "history" | "sync-telemetry";
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
interface DashboardStoreState {
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
export declare const dashboardStore: import("@bugence/core").Store<DashboardStoreState>;
export declare function getDashboardState(): DashboardStoreState;
export declare function useDashboardStore(): import("@bugence/core").Store<DashboardStoreState>;
export declare function loadDashboardPages(options?: {
    force?: boolean;
    ttlMs?: number;
}): Promise<ContentPageListItem[]>;
export declare function selectDashboardPage(pageId?: string): void;
export declare function getSelectedDashboardPage(): ContentPageListItem | null;
export declare function loadDashboardHistory(options?: {
    pageId?: string;
    take?: number;
    force?: boolean;
    ttlMs?: number;
}): Promise<ContentHistoryEntry[]>;
export declare function getDashboardHistory(pageId?: string, take?: number): ContentHistoryEntry[];
export declare function getTimelineSnapshot(pageId?: string): DashboardTimelineSnapshot;
export declare function startDashboardSync(options: {
    pageId: string;
    intervalMs?: number;
}): void;
export declare function stopDashboardSync(): void;
export {};
