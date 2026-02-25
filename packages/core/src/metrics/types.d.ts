export interface MetricContext extends Record<string, unknown> {
    pageId?: string;
    sectionId?: string;
    data?: Record<string, unknown>;
    source?: "dashboard" | "canvas" | "insights" | string;
}
export interface MetricResult<TValue = unknown> {
    id: string;
    value: TValue;
    formatted?: string;
    unit?: string;
    updatedAtUtc?: string | null;
    trend?: "up" | "down" | "flat";
    details?: Record<string, unknown>;
}
export type MetricResolver<TValue = unknown> = (context: MetricContext) => Promise<MetricResult<TValue> | null | undefined> | MetricResult<TValue> | null | undefined;
export type MetricFormatter<TValue = unknown> = (result: MetricResult<TValue>, context: MetricContext) => MetricResult<TValue>;
export interface MetricRegistration<TValue = unknown> {
    id: string;
    title: string;
    description?: string;
    capabilities?: string[];
    tags?: string[];
    resolve: MetricResolver<TValue>;
    format?: MetricFormatter<TValue>;
}
//# sourceMappingURL=types.d.ts.map