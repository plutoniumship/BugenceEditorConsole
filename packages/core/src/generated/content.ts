/* eslint-disable */
/*
 * This file mirrors the Bugence Content API schema.
 * Regenerate with: pnpm exec openapi-typescript BugenceEditConsole/OpenApi/content.openapi.yaml --output packages/core/src/generated/content.ts
 */

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

