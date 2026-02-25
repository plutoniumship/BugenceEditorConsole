export type UuidSchema = string;
export type IsoDateSchema = string;
export interface ContentHistoryEntryDiffSchema {
    changeType: "added" | "removed" | "modified" | "unchanged";
    previousLength: number;
    currentLength: number;
    characterDelta: number;
    containsHtml: boolean;
    snippet?: string | null;
}
export interface ContentHistoryEntrySchema {
    id: UuidSchema;
    sitePageId: UuidSchema;
    pageSectionId?: UuidSchema | null;
    fieldKey: string;
    previousValue?: string | null;
    newValue?: string | null;
    changeSummary?: string | null;
    performedByUserId: string;
    performedByDisplayName?: string | null;
    performedAtUtc: IsoDateSchema;
    diff?: ContentHistoryEntryDiffSchema | null;
}
//# sourceMappingURL=content.d.ts.map