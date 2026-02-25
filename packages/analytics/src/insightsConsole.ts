import { renderTrendChart } from "./charts/lineChart";
import { renderBreakdownList } from "./charts/breakdownList";
import { renderStoryline } from "./storylineRenderer";
import type { AnalyticsPayload } from "./types";

const RANGE_FORMAT = new Intl.DateTimeFormat(undefined, {
  month: "short",
  day: "numeric"
});

function parsePayload(script: HTMLScriptElement): AnalyticsPayload | null {
  const raw = script.textContent?.trim();
  if (!raw) {
    return null;
  }

  try {
    return JSON.parse(raw) as AnalyticsPayload;
  } catch (error) {
    console.warn("[analytics] Failed to parse analytics payload", error);
    return null;
  }
}

function formatRange(start: string, end: string): string {
  const startDate = new Date(start);
  const endDate = new Date(end);
  if (Number.isNaN(startDate.getTime()) || Number.isNaN(endDate.getTime())) {
    return "";
  }

  return `${RANGE_FORMAT.format(startDate)} – ${RANGE_FORMAT.format(endDate)}`;
}

function hydrateAnalytics(root: HTMLElement, payload: AnalyticsPayload): void {
  const trendHost = root.querySelector<HTMLElement>('[data-analytics-chart="trend"]');
  if (trendHost) {
    renderTrendChart(trendHost, payload.trend);
  }

  const editorsHost = root.querySelector<HTMLElement>('[data-analytics-breakdown="editors"]');
  if (editorsHost) {
    renderBreakdownList(editorsHost, payload.topEditors, "No editor activity in range.");
  }

  const fieldsHost = root.querySelector<HTMLElement>('[data-analytics-breakdown="fields"]');
  if (fieldsHost) {
    renderBreakdownList(fieldsHost, payload.topFields, "No field changes captured.");
  }

  const storylineHost = root.querySelector<HTMLElement>('[data-analytics-storyline]');
  if (storylineHost) {
    renderStoryline(storylineHost, payload.storyline);
  }

  const summaryLabel = root.querySelector<HTMLElement>('[data-analytics-summary]');
  if (summaryLabel) {
    const range = formatRange(payload.rangeStartUtc, payload.rangeEndUtc);
    summaryLabel.textContent = range ? `${payload.totalChanges.toLocaleString()} changes · ${range}` : `${payload.totalChanges.toLocaleString()} changes`;
  }

  root.dataset.analyticsReady = "true";
}

function discoverRoots(target: Document | HTMLElement): HTMLElement[] {
  if (target instanceof HTMLElement) {
    return [target].filter((element) => element.matches('[data-analytics-root]'));
  }

  return Array.from(target.querySelectorAll<HTMLElement>('[data-analytics-root]'));
}

export function initInsightsAnalytics(target: Document | HTMLElement = document): void {
  const roots = discoverRoots(target);
  if (!roots.length && target instanceof Document) {
    const fallback = target.querySelector<HTMLElement>('[data-analytics-root]');
    if (fallback) {
      roots.push(fallback);
    }
  }

  roots.forEach((root) => {
    const script = root.querySelector<HTMLScriptElement>('[data-analytics-config]');
    if (!script) {
      return;
    }

    const payload = parsePayload(script);
    if (!payload) {
      return;
    }

    hydrateAnalytics(root, payload);
  });
}
