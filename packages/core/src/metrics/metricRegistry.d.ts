import type { MetricContext, MetricRegistration, MetricResult } from "./types";
export declare function registerMetric(registration: MetricRegistration): void;
export declare function registerMetrics(registrations: MetricRegistration[]): void;
export declare function unregisterMetric(id: string): boolean;
export declare function listMetricMetas(): MetricRegistration[];
export declare function resolveMetric(id: string, context: MetricContext): Promise<MetricResult | null>;
export declare function resolveMetrics(ids: string[], context: MetricContext): Promise<Record<string, MetricResult>>;
//# sourceMappingURL=metricRegistry.d.ts.map