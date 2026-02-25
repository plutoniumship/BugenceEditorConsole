import { hasCapability } from "../capabilities/capabilities";
import type { MetricContext, MetricRegistration, MetricResult } from "./types";

interface RegisteredMetric {
  registration: MetricRegistration;
}

const metrics = new Map<string, RegisteredMetric>();

function normaliseId(id: string): string {
  return id.trim();
}

function ensureRegistration(registration: MetricRegistration): void {
  if (!registration?.id) {
    throw new Error("Metric registration requires an id.");
  }
  if (typeof registration.resolve !== "function") {
    throw new Error(`Metric '${registration.id}' must provide a resolve function.`);
  }
}

function isCapabilitySatisfied(registration: MetricRegistration): boolean {
  if (!registration.capabilities?.length) {
    return true;
  }
  return registration.capabilities.every((capability) => hasCapability(capability));
}

function defaultFormatter(result: MetricResult): MetricResult {
  if (result.formatted) {
    return result;
  }

  const value = result.value;
  if (value === null || value === undefined) {
    return {
      ...result,
      formatted: "--"
    };
  }

  if (typeof value === "number") {
    return {
      ...result,
      formatted: Number.isFinite(value) ? value.toLocaleString() : String(value)
    };
  }

  if (value instanceof Date) {
    return {
      ...result,
      formatted: value.toISOString()
    };
  }

  return {
    ...result,
    formatted: String(value)
  };
}

export function registerMetric(registration: MetricRegistration): void {
  ensureRegistration(registration);
  const id = normaliseId(registration.id);
  if (metrics.has(id)) {
    throw new Error(`Metric '${id}' is already registered.`);
  }
  metrics.set(id, { registration });
}

export function registerMetrics(registrations: MetricRegistration[]): void {
  registrations.forEach(registerMetric);
}

export function unregisterMetric(id: string): boolean {
  return metrics.delete(normaliseId(id));
}

export function listMetricMetas(): MetricRegistration[] {
  return Array.from(metrics.values()).map((entry) => entry.registration);
}

export async function resolveMetric(
  id: string,
  context: MetricContext
): Promise<MetricResult | null> {
  const entry = metrics.get(normaliseId(id));
  if (!entry) {
    return null;
  }

  if (!isCapabilitySatisfied(entry.registration)) {
    return null;
  }

  const resolved = await Promise.resolve(entry.registration.resolve(context));
  if (!resolved) {
    return null;
  }

  const result: MetricResult = {
    ...resolved,
    id: resolved.id || entry.registration.id
  };

  const formatted =
    entry.registration.format?.(result, context) ?? defaultFormatter(result);
  return formatted;
}

export async function resolveMetrics(
  ids: string[],
  context: MetricContext
): Promise<Record<string, MetricResult>> {
  const resolvedEntries = await Promise.all(
    ids.map(async (id) => {
      const result = await resolveMetric(id, context);
      return [id, result] as const;
    })
  );

  return resolvedEntries.reduce<Record<string, MetricResult>>((acc, [id, result]) => {
    if (result) {
      acc[id] = result;
    }
    return acc;
  }, {});
}
