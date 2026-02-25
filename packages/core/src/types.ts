export type Uuid = string;
export type IsoDateString = string;

export type SectionContentType = "Text" | "Html" | "Image" | "RichText";

export interface SitePage {
  id: Uuid;
  name: string;
  slug: string;
  description?: string | null;
  heroImagePath?: string | null;
  createdAtUtc: IsoDateString;
  updatedAtUtc: IsoDateString;
  sections: PageSection[];
}

export interface PageSection {
  id: Uuid;
  sitePageId: Uuid;
  sectionKey: string;
  title?: string | null;
  contentType: SectionContentType;
  contentValue?: string | null;
  cssSelector?: string | null;
  mediaPath?: string | null;
  mediaAltText?: string | null;
  displayOrder: number;
  isLocked: boolean;
  updatedAtUtc: IsoDateString;
  lastPublishedAtUtc?: IsoDateString | null;
}

export interface PageSectionWithHistory extends Omit<PageSection, "sitePageId"> {
  previousContentValue?: string | null;
  etag?: string | null;
}

export interface ContentChangeLog {
  id: Uuid;
  sitePageId: Uuid;
  pageSectionId?: Uuid | null;
  fieldKey: string;
  previousValue?: string | null;
  newValue?: string | null;
  changeSummary?: string | null;
  performedByUserId: string;
  performedByDisplayName?: string | null;
  performedAtUtc: IsoDateString;
}

export interface PageSummary {
  id: Uuid;
  name: string;
  slug: string;
  description?: string | null;
}

export interface ContentPageListItem extends PageSummary {
  updatedAtUtc: IsoDateString;
  lastPublishedAtUtc?: IsoDateString | null;
  sectionCount: number;
  textSections: number;
  imageSections: number;
}

export interface PageCollectionTotals {
  pageCount: number;
  sectionCount: number;
  textSections: number;
  imageSections: number;
  latestUpdateUtc?: IsoDateString | null;
}

export interface PageCollectionResponse {
  pages: ContentPageListItem[];
  totals: PageCollectionTotals;
  etag?: string | null;
}

export interface ContentPageResponse {
  page: PageSummary;
  sections: PageSectionWithHistory[];
  etag?: string | null;
  sectionsEtag?: string | null;
  pageEtag?: string | null;
}

export interface SectionsResponse {
  page: {
    id: Uuid;
    name: string;
    slug: string;
    description?: string | null;
    updatedAtUtc: IsoDateString;
    lastPublishedAtUtc?: IsoDateString | null;
  };
  sections: PageSectionWithHistory[];
  etag?: string | null;
  pageEtag?: string | null;
}

export interface ContentHistoryEntry {
  id: Uuid;
  sitePageId: Uuid;
  pageSectionId?: Uuid | null;
  fieldKey: string;
  previousValue?: string | null;
  newValue?: string | null;
  changeSummary?: string | null;
  performedByUserId: string;
  performedByDisplayName?: string | null;
  performedAtUtc: IsoDateString;
  diff?: ContentHistoryEntryDiff | null;
}

export interface ContentHistoryResponse {
  history: ContentHistoryEntry[];
  etag?: string | null;
}

export interface ContentHistoryEntryDiff {
  changeType: DiffChangeType;
  previousLength: number;
  currentLength: number;
  characterDelta: number;
  containsHtml: boolean;
  snippet?: string | null;
}

export interface SectionMutationResponse {
  message: string | null;
  section: PageSectionWithHistory | null;
  pageEtag?: string | null;
}

export interface PublishResponse {
  message: string;
  publishedAtUtc: IsoDateString;
  warnings: string[];
  etag?: string | null;
}

export interface DeleteSectionResponse {
  message: string;
  section: Pick<PageSectionWithHistory, "id" | "sectionKey" | "contentType" | "cssSelector" | "updatedAtUtc"> | null;
  pageEtag?: string | null;
}

export interface EditorSelectorHints {
  [sectionKey: string]: string;
}

export interface EditorConfig {
  pageId: Uuid;
  pageSlug: string;
  pageName: string;
  apiBase: string;
  selectorHints?: EditorSelectorHints;
  pageAsset: string;
  editUrl: string;
}

export interface SectionFormPayload {
  sectionId?: Uuid;
  selector?: string | null;
  contentType?: SectionContentType;
  contentValue?: string | null;
  mediaAltText?: string | null;
  image?: File | null;
}

export interface SectionUpdatePayload extends SectionFormPayload {
  sectionId: Uuid;
}

export interface SectionCreatePayload extends SectionFormPayload {
  selector: string;
  contentType: SectionContentType;
  sectionId?: Uuid;
}

export type SectionUpsertPayload = SectionFormPayload;

export interface SectionDraftState {
  id: Uuid;
  contentValue?: string | null;
  mediaPath?: string | null;
  mediaAltText?: string | null;
  lastSavedAtUtc?: IsoDateString | null;
  hasUnpublishedChanges: boolean;
}

export interface CanvasSnapshot {
  page: SectionsResponse["page"];
  sections: PageSectionWithHistory[];
  retrievedAt: IsoDateString;
}

export interface SnapshotEnvelope {
  pageId: Uuid;
  sectionId: Uuid;
  selector?: string | null;
  changeVersion: number;
  capturedAtUtc: IsoDateString;
  contentHash: string;
  etag?: string | null;
  payload: PageSectionWithHistory;
}

export type DiffChangeType = "added" | "removed" | "modified" | "unchanged";

export interface DiffAnnotation {
  code: string;
  message: string;
  severity: "info" | "warning" | "error";
  field?: string;
}

export interface DiffEnvelope {
  pageId: Uuid;
  sectionId: Uuid;
  changeType: DiffChangeType;
  before?: SnapshotEnvelope;
  after?: SnapshotEnvelope;
  conflict?: boolean;
  annotations?: DiffAnnotation[];
  resolvedAtUtc?: IsoDateString | null;
}

export interface PublishSummaryEntry {
  sectionId: Uuid;
  sectionKey: string;
  contentType: SectionContentType;
  changeType: DiffChangeType;
  reviewerStatus?: ReviewStatus;
  reviewerComment?: string | null;
  diffId: string;
  diffSummary?: string | null;
}

export interface PublishSummary {
  pageId: Uuid;
  generatedAtUtc: IsoDateString;
  entries: PublishSummaryEntry[];
  reviewer?: {
    id: Uuid;
    displayName?: string | null;
  };
  notes?: string | null;
}

export type ReviewStatus = "pending" | "approved" | "rejected";

export interface EventEnvelope<TTopic extends string = string, TPayload = unknown> {
  id: string;
  topic: TTopic;
  payload: TPayload;
  timestamp: IsoDateString;
  source: string;
}

export type EventListener<TTopic extends string = string, TPayload = unknown> = (
  envelope: EventEnvelope<TTopic, TPayload>
) => void;

export interface EventBus<TTopic extends string = string, TPayload = unknown> {
  publish(topic: TTopic, payload: TPayload): EventEnvelope<TTopic, TPayload>;
  subscribe(listener: EventListener<TTopic, TPayload>): () => void;
  close(): void;
  getId(): string;
}

export interface CanvasError {
  message: string;
  status?: number;
  details?: unknown;
  sectionId?: Uuid;
}

export interface CanvasStoreState extends Record<string, unknown> {
  page: SectionsResponse["page"] | null;
  sections: PageSectionWithHistory[];
  snapshot?: CanvasSnapshot | null;
  baseline: Record<Uuid, SnapshotEnvelope>;
  dirtySectionIds: Uuid[];
  reviewStatuses: Record<Uuid, ReviewStatus>;
  pageId?: Uuid;
  isLoading?: boolean;
  isSaving: boolean;
  isPublishing: boolean;
  error?: CanvasError | null;
  pageEtag?: string | null;
  sectionsEtag?: string | null;
  history: ContentHistoryEntry[];
  historyEtag?: string | null;
  optimisticSectionIds: Uuid[];
  conflictSectionIds: Uuid[];
  lastFetchedAtUtc?: IsoDateString | null;
  lastHistoryFetchedAtUtc?: IsoDateString | null;
  lastSyncedAtUtc?: IsoDateString | null;
}

export type CanvasStoreListener = (state: CanvasStoreState) => void;

export interface CanvasStore {
  getState(): CanvasStoreState;
  subscribe(listener: CanvasStoreListener): () => void;
  setState(partial: Partial<CanvasStoreState> | ((state: CanvasStoreState) => Partial<CanvasStoreState>)): void;
  reset(): void;
}

export type SectionFieldType = "text" | "richtext" | "html" | "image" | "media" | "toggle" | "select";

export interface SectionFieldSchema {
  id: string;
  type: SectionFieldType;
  label: string;
  helperText?: string;
  placeholder?: string;
  required?: boolean;
  maxLength?: number;
  toolbar?: string[];
  allowedTags?: string[];
  allowedAttributes?: string[];
  sanitizer?: "basic" | "strict" | "none";
  previewHint?: string;
  accept?: string[];
  maxFileSizeMB?: number;
}

export interface SectionSchema {
  id: string;
  title: string;
  description?: string;
  contentType: SectionContentType;
  fields: SectionFieldSchema[];
  preview?: {
    visualType: "text" | "metric" | "image" | "cta" | "custom";
    summaryField?: string;
    secondaryField?: string;
  };
  defaults?: {
    contentValue?: string;
    mediaAltText?: string;
  };
}

export interface SidebarMetricSchema {
  id: string;
  label: string;
  description?: string;
  icon?: string;
  compute: "static" | "dynamic";
  valueField?: string;
  formatter?: "number" | "percentage" | "datetime" | "string";
  emphasis?: "default" | "positive" | "warning";
}

export interface SidebarActionSchema {
  id: string;
  label: string;
  icon?: string;
  description?: string;
  intent?: "primary" | "secondary" | "danger";
  target: "modal" | "drawer" | "link" | "command";
  command?: string;
  href?: string;
}

export interface SidebarCardSchema {
  id: string;
  title: string;
  description?: string;
  metrics?: SidebarMetricSchema[];
  actions?: SidebarActionSchema[];
  footerHint?: string;
}

export interface WorkflowStepSchema {
  id: string;
  label: string;
  description?: string;
  icon?: string;
  status: "draft" | "review" | "publish";
  blockers?: string[];
  actions?: SidebarActionSchema[];
}

export interface WorkflowSchema {
  id: string;
  title: string;
  steps: WorkflowStepSchema[];
  defaultStepId: string;
}

export interface SectionSchemaRegistry {
  [sectionKey: string]: SectionSchema;
}

export interface SidebarCardRegistry {
  [cardId: string]: SidebarCardSchema;
}

export interface WorkflowRegistry {
  [workflowId: string]: WorkflowSchema;
}
