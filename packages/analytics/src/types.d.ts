export interface TrendPoint {
    dayUtc: string;
    changes: number;
    uniqueEditors: number;
}
export interface BreakdownEntry {
    label: string;
    changes: number;
}
export interface StorylineEvent {
    title: string;
    highlightUtc: string;
    summary: string;
    icon: string;
    changes?: number | null;
    accent?: string | null;
}
export interface AnalyticsPayload {
    rangeStartUtc: string;
    rangeEndUtc: string;
    totalChanges: number;
    trend: TrendPoint[];
    topEditors: BreakdownEntry[];
    topFields: BreakdownEntry[];
    storyline: StorylineEvent[];
}
//# sourceMappingURL=types.d.ts.map