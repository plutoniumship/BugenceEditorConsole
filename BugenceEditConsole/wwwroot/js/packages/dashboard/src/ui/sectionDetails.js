import { getDashboardHistory, getTimelineSnapshot, loadDashboardHistory, useDashboardStore } from "../store/dashboardStore";
const DATE_WITH_TIME = new Intl.DateTimeFormat(undefined, {
    month: "short",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit"
});
export function initSectionDetails() {
    const pageRoot = document.querySelector("[data-dashboard-page]");
    if (!pageRoot) {
        return;
    }
    const pageId = pageRoot.getAttribute("data-dashboard-page");
    if (!pageId) {
        return;
    }
    const cards = Array.from(document.querySelectorAll("[data-editor-card]"));
    if (!cards.length) {
        return;
    }
    const contexts = cards
        .map((card) => setupCardContext(card, pageId))
        .filter((context) => context !== null);
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
    window.addEventListener("beforeunload", () => {
        unsubscribe();
    }, { once: true });
}
function setupCardContext(card, pageId) {
    const sectionId = card.getAttribute("data-section-id");
    if (!sectionId) {
        return null;
    }
    const tabs = Array.from(card.querySelectorAll("[data-section-tabs] [data-section-tab]"));
    const panels = Array.from(card.querySelectorAll("[data-section-panel]"));
    const context = {
        card,
        pageId,
        sectionId,
        tabs,
        panels,
        metaHost: card.querySelector("[data-section-meta]"),
        flagsHost: card.querySelector("[data-section-flags]"),
        actionsHost: card.querySelector("[data-section-actions]"),
        historyList: card.querySelector("[data-section-history-list]"),
        historyEmpty: card.querySelector("[data-section-history-empty]"),
        activeTab: "editor",
        detailsPopulated: false,
        historyLoaded: false
    };
    tabs.forEach((tab) => {
        tab.addEventListener("click", () => {
            const key = tab.getAttribute("data-section-tab");
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
function updateContextFromTimeline(context, snapshot) {
    if (context.activeTab === "details") {
        populateDetails(context, snapshot);
    }
    else {
        context.detailsPopulated = false;
    }
    if (context.activeTab === "history" && context.historyLoaded) {
        void populateHistory(context);
    }
}
function updateTabVisualState(context) {
    context.tabs.forEach((tab) => {
        const key = tab.getAttribute("data-section-tab");
        tab.dataset.state = key === context.activeTab ? "active" : "idle";
    });
    context.panels.forEach((panel) => {
        const key = panel.getAttribute("data-section-panel");
        panel.dataset.state = key === context.activeTab ? "active" : "idle";
    });
}
function populateDetails(context, snapshot = getTimelineSnapshot(context.pageId)) {
    const { metaHost, flagsHost, actionsHost, card, sectionId } = context;
    if (!metaHost || !flagsHost || !actionsHost) {
        return;
    }
    const dataset = card.dataset;
    const timelineDirty = snapshot.dirtySections[sectionId];
    const reviewStatus = snapshot.reviewStatuses[sectionId];
    const isConflicted = snapshot.conflictSectionIds.includes(sectionId);
    const rows = [
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
    }
    else {
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
    const flags = [];
    if (dataset.sectionLocked === "true") {
        flags.push("Locked by concierge");
    }
    else {
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
    }
    else {
        const message = document.createElement("p");
        message.className = "section-card__diff-empty";
        message.textContent = "No active diff recorded for this section.";
        actionsHost.appendChild(message);
    }
    context.detailsPopulated = true;
}
async function populateHistory(context) {
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
    }
    catch {
        // swallow fetch errors; store may already have data
    }
    const entries = getDashboardHistory(pageId, 60).filter((entry) => entry.pageSectionId === sectionId);
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
function buildDiffSummary(diff) {
    const sectionKey = diff.after?.payload.sectionKey ??
        diff.before?.payload.sectionKey ??
        diff.sectionId;
    const summaryParts = [`${capitalize(diff.changeType)} · ${sectionKey}`];
    if (diff.annotations?.length) {
        summaryParts.push(diff.annotations.join("; "));
    }
    return summaryParts.join(" — ");
}
function buildHistorySummary(entry) {
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
function formatDate(value) {
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? value : DATE_WITH_TIME.format(date);
}
function formatReviewStatus(status) {
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
function capitalize(value) {
    return value.charAt(0).toUpperCase() + value.slice(1);
}
function truncate(value, length = 60) {
    const next = value.replace(/\s+/g, " ").trim();
    return next.length > length ? `${next.slice(0, length)}…` : next;
}
