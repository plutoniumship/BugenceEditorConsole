import type {
  DiffEnvelope,
  EventEnvelope,
  PublishSummary,
  ReviewStatus,
  SnapshotEnvelope,
  Uuid
} from "../types";

export type SyncEventTopic =
  | "snapshot.applied"
  | "section.changed"
  | "section.committed"
  | "section.conflicted"
  | "review.status.updated"
  | "timeline.publish.summary"
  | "sync.telemetry";

export interface SnapshotAppliedPayload {
  pageId: Uuid;
  snapshot: SnapshotEnvelope[];
  source: "remote" | "local";
  appliedAtUtc: string;
}

export interface SectionChangedPayload {
  pageId: Uuid;
  sectionId: Uuid;
  diff: DiffEnvelope | null;
  dirty: boolean;
}

export interface SectionCommittedPayload {
  pageId: Uuid;
  sectionId: Uuid;
  diff: DiffEnvelope | null;
  committedAtUtc: string;
}

export interface SectionConflictPayload {
  pageId: Uuid;
  sectionId: Uuid;
  diff: DiffEnvelope | null;
  detectedAtUtc: string;
}

export interface ReviewStatusPayload {
  pageId: Uuid;
  sectionId: Uuid;
  status: ReviewStatus;
  reviewerId?: Uuid;
  reviewerName?: string | null;
  comment?: string | null;
  updatedAtUtc: string;
}

export interface PublishSummaryPayload {
  pageId: Uuid;
  summary: PublishSummary;
  preparedAtUtc: string;
}

export interface SyncTelemetryPayload {
  pageId: Uuid;
  durationMs: number;
  result: "success" | "noop" | "error";
  source: "dashboard" | "canvas";
  errorMessage?: string;
}

export interface SyncEventPayloadMap {
  "snapshot.applied": SnapshotAppliedPayload;
  "section.changed": SectionChangedPayload;
  "section.committed": SectionCommittedPayload;
  "section.conflicted": SectionConflictPayload;
  "review.status.updated": ReviewStatusPayload;
  "timeline.publish.summary": PublishSummaryPayload;
  "sync.telemetry": SyncTelemetryPayload;
}

export type SyncEventEnvelope<TTopic extends SyncEventTopic = SyncEventTopic> = EventEnvelope<
  TTopic,
  SyncEventPayloadMap[TTopic]
>;
