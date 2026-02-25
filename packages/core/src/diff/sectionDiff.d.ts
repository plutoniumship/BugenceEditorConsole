import type { DiffEnvelope, IsoDateString, PageSectionWithHistory, SnapshotEnvelope, Uuid } from "../types";
interface SnapshotOptions {
    changeVersion?: number;
    capturedAtUtc?: IsoDateString;
    etag?: string | null;
}
export interface DiffOptions {
    detectConflicts?: boolean;
}
export declare function createSnapshotEnvelope(pageId: Uuid, section: PageSectionWithHistory, options?: SnapshotOptions): SnapshotEnvelope;
export declare function createSectionDiff(before: SnapshotEnvelope | null, after: SnapshotEnvelope | null, options?: DiffOptions): DiffEnvelope | null;
export declare function diffSnapshotSets(before: SnapshotEnvelope[], after: SnapshotEnvelope[], options?: DiffOptions): DiffEnvelope[];
export {};
//# sourceMappingURL=sectionDiff.d.ts.map