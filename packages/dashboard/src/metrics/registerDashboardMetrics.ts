import { registerMetrics, type MetricRegistration } from "@bugence/core";

function toNumber(value: unknown, fallback = 0): number {
  const parsed = typeof value === "string" ? Number(value) : Number(value);
  return Number.isFinite(parsed) ? parsed : fallback;
}

const dashboardMetricRegistrations: MetricRegistration[] = [
  {
    id: "draftCount",
    title: "Draft sections",
    description: "Sections with unpublished changes.",
    capabilities: ["dashboard.metrics.draftCount"],
    resolve: (context) => {
      const value = toNumber(context.data?.draftCount, 0);
      return {
        id: "draftCount",
        value,
        updatedAtUtc: context.data?.lastSyncedAtUtc as string | undefined
      };
    },
    format: (result) => ({
      ...result,
      formatted: result.value.toString()
    })
  },
  {
    id: "lastPublishedAtUtc",
    title: "Last publish",
    description: "Timestamp of the most recent publish event.",
    capabilities: ["dashboard.metrics.lastPublished"],
    resolve: (context) => {
      const raw = context.data?.lastPublishedAtUtc;
      return {
        id: "lastPublishedAtUtc",
        value: raw ?? null
      };
    },
    format: (result) => {
      const value = result.value;
      if (!value) {
        return {
          ...result,
          formatted: "Not yet published"
        };
      }

      try {
        const date = new Date(value as string);
        return {
          ...result,
          formatted: date.toLocaleString()
        };
      } catch {
        return {
          ...result,
          formatted: String(value)
        };
      }
    }
  },
  {
    id: "accessibilityScore",
    title: "Accessibility score",
    description: "Latest automated accessibility audit score.",
    capabilities: ["dashboard.metrics.accessibility"],
    resolve: (context) => {
      const value = toNumber(context.data?.accessibilityScore, 0);
      return {
        id: "accessibilityScore",
        value
      };
    },
    format: (result) => {
      const value = typeof result.value === "number" ? result.value : Number(result.value);
      if (!Number.isFinite(value)) {
        return { ...result, formatted: "--" };
      }
      const clamped = Math.min(Math.max(value, 0), 1);
      return {
        ...result,
        formatted: `${Math.round(clamped * 100)}%`
      };
    }
  },
  {
    id: "brokenLinkCount",
    title: "Broken links",
    description: "Links detected as unreachable during the last scan.",
    capabilities: ["dashboard.metrics.brokenLinks"],
    resolve: (context) => {
      const value = toNumber(context.data?.brokenLinkCount, 0);
      return {
        id: "brokenLinkCount",
        value
      };
    },
    format: (result) => ({
      ...result,
      formatted: result.value.toString()
    })
  }
];

export function registerDashboardMetrics(options: { force?: boolean } = {}): void {
  if (options.force) {
    // When forcing registration we re-register to ensure latest handlers.
    registerMetrics(dashboardMetricRegistrations);
    return;
  }

  try {
    registerMetrics(dashboardMetricRegistrations);
  } catch {
    // Silently ignore duplicate registrations.
  }
}

registerDashboardMetrics();

