import type {
  ContentHistoryEntry,
  DiffEnvelope,
  ReviewStatus
} from "@bugence/core";
import {
  getDashboardHistory,
  getTimelineSnapshot,
  loadDashboardHistory,
  useDashboardStore
} from "../store/dashboardStore";

type TabKey = "editor" | "details" | "history";

interface SectionCardContext {
  card: HTMLElement;
  pageId: string;
  sectionId: string;
  tabs: HTMLButtonElement[];
  panels: HTMLElement[];
  metaHost: HTMLElement | null;
  flagsHost: HTMLElement | null;
  actionsHost: HTMLElement | null;
  historyList: HTMLElement | null;
  historyEmpty: HTMLElement | null;
  activeTab: TabKey;
  detailsPopulated: boolean;
  historyLoaded: boolean;
}

const DATE_WITH_TIME = new Intl.DateTimeFormat(undefined, {
  month: "short",
  day: "2-digit",
  hour: "2-digit",
  minute: "2-digit"
});

export function initSectionDetails() {
  const pageRoot = document.querySelector<HTMLElement>("[data-dashboard-page]");
  if (!pageRoot) {
    return;
  }

  const pageId = pageRoot.getAttribute("data-dashboard-page");
  if (!pageId) {
    return;
  }

  const cards = Array.from(document.querySelectorAll<HTMLElement>("[data-editor-card]"));
  if (!cards.length) {
    return;
  }

  const contexts = cards
    .map((card): SectionCardContext | null => setupCardContext(card, pageId))
    .filter((context): context is SectionCardContext => context !== null);

  if (!contexts.length) {
    return;
  }

  const store = useDashboardStore();

  const refresh = () => {
    const snapshot = getTimelineSnapshot(pageId);
    contexts.forEach((context) => updateContextFromTimeline(context, snapshot));
  };

  refresh();
  const unsubscribe = store.subscribe(refresh);

  window.addEventListener(
    "beforeunload",
    () => {
      unsubscribe();
    },
    { once: true }
  );
}

function setupCardContext(card: HTMLElement, pageId: string): SectionCardContext | null {
  const sectionId = card.getAttribute("data-section-id");
  if (!sectionId) {
    return null;
  }

  const tabs = Array.from(
    card.querySelectorAll<HTMLButtonElement>("[data-section-tabs] [data-section-tab]")
  );
  const panels = Array.from(
    card.querySelectorAll<HTMLElement>("[data-section-panel]")
  );

  const context: SectionCardContext = {
    card,
    pageId,
    sectionId,
    tabs,
    panels,
    metaHost: card.querySelector<HTMLElement>("[data-section-meta]"),
    flagsHost: card.querySelector<HTMLElement>("[data-section-flags]"),
    actionsHost: card.querySelector<HTMLElement>("[data-section-actions]"),
    historyList: card.querySelector<HTMLElement>("[data-section-history-list]"),
    historyEmpty: card.querySelector<HTMLElement>("[data-section-history-empty]"),
    activeTab: "editor",
    detailsPopulated: false,
    historyLoaded: false
  };

  tabs.forEach((tab) => {
    tab.addEventListener("click", () => {
      const key = tab.getAttribute("data-section-tab") as TabKey | null;
      if (!key || context.activeTab === key) {
        return;
      }

      context.activeTab = key;
      updateTabVisualState(context);

      if (key === "details" && !context.detailsPopulated) {
        populateDetails(context);
      }

      if (key === "history" && !context.historyLoaded) {
        void populateHistory(context);
      }
    });
  });

  return context;
}

function updateContextFromTimeline(
  context: SectionCardContext,
  snapshot: ReturnType<typeof getTimelineSnapshot>
) {
  if (context.activeTab === "details") {
    populateDetails(context, snapshot);
  } else {
    context.detailsPopulated = false;
  }

  if (context.activeTab === "history" && context.historyLoaded) {
    void populateHistory(context);
  }
}

function updateTabVisualState(context: SectionCardContext) {
  context.tabs.forEach((tab) => {
    const key = tab.getAttribute("data-section-tab");
    tab.dataset.state = key === context.activeTab ? "active" : "idle";
  });

  context.panels.forEach((panel) => {
    const key = panel.getAttribute("data-section-panel");
    panel.dataset.state = key === context.activeTab ? "active" : "idle";
  });
}

function populateDetails(
  context: SectionCardContext,
  snapshot = getTimelineSnapshot(context.pageId)
) {
  const { metaHost, flagsHost, actionsHost, card, sectionId } = context;
  if (!metaHost || !flagsHost || !actionsHost) {
    return;
  }

  const dataset = card.dataset;
  const timelineDirty = snapshot.dirtySections[sectionId];
  const reviewStatus = snapshot.reviewStatuses[sectionId];
  const isConflicted = snapshot.conflictSectionIds.includes(sectionId);

  const rows: Array<[string, string]> = [
    ["Key", dataset.sectionKey ?? ""],
    ["Selector", dataset.sectionSelector ?? "No selector"],
    ["Content type", dataset.sectionType ?? "Unknown"],
    ["Display order", dataset.sectionOrder ?? "—"]
  ];

  if (dataset.sectionUpdated) {
    rows.push(["Last updated", formatDate(dataset.sectionUpdated)]);
  }

  if (dataset.sectionPublished) {
    rows.push(["Last published", formatDate(dataset.sectionPublished)]);
  } else {
    rows.push(["Last published", "Not yet published"]);
  }

  metaHost.innerHTML = "";
  rows.forEach(([label, value]) => {
    const dt = document.createElement("dt");
    dt.textContent = label;
    const dd = document.createElement("dd");
    dd.textContent = value;
    metaHost.append(dt, dd);
  });

  flagsHost.innerHTML = "";
  const flags: string[] = [];
  if (dataset.sectionLocked === "true") {
    flags.push("Locked by concierge");
  } else {
    flags.push("Editable");
  }

  if (dataset.sectionHasDraft === "true") {
    flags.push("Draft ahead of publish");
  }

  if (timelineDirty?.dirty) {
    flags.push("Unsaved diff detected");
  }

  if (isConflicted) {
    flags.push("Conflict with remote baseline");
  }

  if (reviewStatus) {
    flags.push(`Review status: ${formatReviewStatus(reviewStatus)}`);
  }

  flags.forEach((flag) => {
    const badge = document.createElement("span");
    badge.className = "section-card__flag";
    badge.textContent = flag;
    flagsHost.appendChild(badge);
  });

  actionsHost.innerHTML = "";
  if (timelineDirty?.diff) {
    const diff = timelineDirty.diff;
    const summary = document.createElement("article");
    summary.className = "section-card__diff";

    const title = document.createElement("h4");
    title.textContent = `Diff · ${capitalize(diff.changeType)}`;

    const body = document.createElement("p");
    body.textContent = buildDiffSummary(diff);

    summary.append(title, body);
    actionsHost.appendChild(summary);
  } else {
    const message = document.createElement("p");
    message.className = "section-card__diff-empty";
    message.textContent = "No active diff recorded for this section.";
    actionsHost.appendChild(message);
  }

  context.detailsPopulated = true;
}

async function populateHistory(context: SectionCardContext) {
  const { pageId, sectionId, historyList, historyEmpty } = context;
  if (!historyList) {
    return;
  }

  if (historyEmpty) {
    historyEmpty.hidden = true;
  }
  historyList.innerHTML = "";

  try {
    await loadDashboardHistory({ pageId, take: 60, force: !context.historyLoaded, ttlMs: 0 });
  } catch {
    // swallow fetch errors; store may already have data
  }

  const entries = getDashboardHistory(pageId, 60).filter(
    (entry) => entry.pageSectionId === sectionId
  );

  if (!entries.length) {
    if (historyEmpty) {
      historyEmpty.hidden = false;
    }
    context.historyLoaded = true;
    return;
  }

  const fragment = document.createDocumentFragment();
  entries.slice(0, 6).forEach((entry) => {
    const item = document.createElement("li");
    item.className = "section-card__history-item";

    const header = document.createElement("div");
    header.className = "section-card__history-head";
    const title = document.createElement("strong");
    title.textContent = entry.changeSummary ?? entry.fieldKey ?? "Content update";
    const meta = document.createElement("span");
    meta.textContent = `${entry.performedByDisplayName ?? "Unknown"} · ${formatDate(entry.performedAtUtc)}`;
    header.append(title, meta);

    const details = document.createElement("div");
    details.className = "section-card__history-body";
    details.textContent = buildHistorySummary(entry);

    item.append(header, details);
    fragment.appendChild(item);
  });

  historyList.appendChild(fragment);
  context.historyLoaded = true;
}

function buildDiffSummary(diff: DiffEnvelope): string {
  const sectionKey =
    diff.after?.payload.sectionKey ??
    diff.before?.payload.sectionKey ??
    diff.sectionId;
  const summaryParts = [`${capitalize(diff.changeType)} · ${sectionKey}`];
  if (diff.annotations?.length) {
    summaryParts.push(diff.annotations.join("; "));
  }
  return summaryParts.join(" — ");
}

function buildHistorySummary(entry: ContentHistoryEntry): string {
  if (entry.changeSummary) {
    return entry.changeSummary;
  }

  const previous = entry.previousValue ?? "";
  const current = entry.newValue ?? "";

  if (previous && current && previous !== current) {
    return `Modified content to "${truncate(current)}"`;
  }

  if (!previous && current) {
    return `Added "${truncate(current)}"`;
  }

  if (previous && !current) {
    return `Removed "${truncate(previous)}"`;
  }

  return "No content changes recorded.";
}

function formatDate(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : DATE_WITH_TIME.format(date);
}

function formatReviewStatus(status: ReviewStatus): string {
  switch (status) {
    case "approved":
      return "Approved";
    case "rejected":
      return "Needs revision";
    case "pending":
    default:
      return "Pending review";
  }
}

function capitalize(value: string): string {
  return value.charAt(0).toUpperCase() + value.slice(1);
}

function truncate(value: string, length = 60): string {
  const next = value.replace(/\s+/g, " ").trim();
  return next.length > length ? `${next.slice(0, length)}…` : next;
}
