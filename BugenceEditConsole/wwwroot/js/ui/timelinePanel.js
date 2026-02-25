import { getTimelineSnapshot, selectDashboardPage, startDashboardSync, stopDashboardSync, useDashboardStore } from "../store/dashboardStore";
const SOURCE_LABEL = {
    canvas: "Canvas",
    dashboard: "Dashboard",
    history: "History",
    workflow: "Workflow"
};
const REVIEW_LABEL = {
    approved: "Approved",
    rejected: "Rejected",
    pending: "Pending review"
};
export function initTimelinePanel() {
    const root = document.querySelector("[data-dashboard-timeline]");
    if (!root) {
        return;
    }
    const pageId = root.getAttribute("data-page-id")?.trim();
    if (!pageId) {
        return;
    }
    const eventsHost = root.querySelector("[data-timeline-events]");
    const emptyState = root.querySelector("[data-timeline-empty]");
    const lastSyncEl = root.querySelector("[data-timeline-last-sync]");
    const summaryList = root.querySelector("[data-publish-summary-list]");
    const summaryEmpty = root.querySelector("[data-publish-summary-empty]");
    const sectionCards = collectSectionCards();
    const store = useDashboardStore();
    const render = () => {
        const snapshot = getTimelineSnapshot(pageId);
        renderTimeline(eventsHost, emptyState, snapshot);
        renderPublishSummary(summaryList, summaryEmpty, snapshot);
        updateLastSync(lastSyncEl, snapshot);
        updateSectionIndicators(sectionCards, snapshot);
    };
    const unsubscribe = store.subscribe(render);
    selectDashboardPage(pageId);
    startDashboardSync({ pageId });
    render();
    const teardown = () => {
        unsubscribe();
        stopDashboardSync();
    };
    window.addEventListener("beforeunload", () => {
        teardown();
    }, { once: true });
    root.addEventListener("dashboard:timeline:dispose", () => {
        teardown();
    }, { once: true });
}
function collectSectionCards() {
    const map = new Map();
    document.querySelectorAll("[data-editor-card]").forEach((card) => {
        const sectionId = card.getAttribute("data-section-id");
        if (!sectionId) {
            return;
        }
        map.set(sectionId, {
            root: card,
            dirtyBadge: card.querySelector("[data-section-dirty-indicator]") ?? undefined,
            conflictBadge: card.querySelector("[data-section-conflict-indicator]") ?? undefined,
            reviewBadge: card.querySelector("[data-section-review-indicator]") ?? undefined,
            reviewLabel: card.querySelector("[data-section-review-label]") ?? undefined
        });
    });
    return map;
}
function renderTimeline(host, empty, snapshot) {
    if (!host) {
        return;
    }
    host.innerHTML = "";
    const events = snapshot.events;
    if (!events.length) {
        if (empty) {
            empty.hidden = false;
        }
        return;
    }
    if (empty) {
        empty.hidden = true;
    }
    events.forEach((event) => {
        host.appendChild(createTimelineEventNode(event));
    });
}
function createTimelineEventNode(event) {
    const article = document.createElement("article");
    article.className = "dashboard-card__event timeline-event";
    article.dataset.tone = event.tone;
    if (event.sectionKey) {
        article.dataset.sectionKey = event.sectionKey;
    }
    const title = document.createElement("h3");
    title.className = "dashboard-card__event-title";
    title.textContent = event.title;
    article.appendChild(title);
    const meta = document.createElement("div");
    meta.className = "dashboard-card__event-meta";
    meta.textContent = SOURCE_LABEL[event.source] ?? "Timeline";
    article.appendChild(meta);
    const timestamp = document.createElement("time");
    timestamp.className = "dashboard-card__event-timestamp";
    timestamp.dateTime = event.timestamp;
    timestamp.textContent = formatEventTime(event.timestamp);
    article.appendChild(timestamp);
    if (event.description) {
        const description = document.createElement("p");
        description.textContent = event.description;
        article.appendChild(description);
    }
    return article;
}
function renderPublishSummary(host, empty, snapshot) {
    if (!host) {
        return;
    }
    host.innerHTML = "";
    const summary = snapshot.publishSummary;
    if (!summary || summary.entries.length === 0) {
        if (empty) {
            empty.hidden = false;
        }
        return;
    }
    if (empty) {
        empty.hidden = true;
    }
    summary.entries.forEach((entry) => {
        const item = document.createElement("li");
        item.className = "dashboard-card__event timeline-event";
        item.dataset.tone = entry.changeType === "removed" ? "warning" : "info";
        const title = document.createElement("h3");
        title.className = "dashboard-card__event-title";
        title.textContent = `${entry.sectionKey} · ${formatChangeType(entry.changeType)}`;
        item.appendChild(title);
        const meta = document.createElement("div");
        meta.className = "dashboard-card__event-meta";
        meta.textContent = entry.contentType ?? "Content";
        if (entry.reviewerStatus) {
            const status = document.createElement("span");
            status.className = "timeline-summary__status";
            status.textContent = REVIEW_LABEL[entry.reviewerStatus] ?? entry.reviewerStatus;
            meta.appendChild(document.createTextNode(" · "));
            meta.appendChild(status);
        }
        item.appendChild(meta);
        host.appendChild(item);
    });
}
function updateLastSync(element, snapshot) {
    if (!element) {
        return;
    }
    const telemetry = snapshot.lastSyncTelemetry;
    if (telemetry?.result === "error") {
        element.dataset.state = "error";
        element.textContent = `Sync error · ${telemetry.errorMessage ?? "Check logs"}`;
        return;
    }
    element.removeAttribute("data-state");
    if (!snapshot.lastSyncedAtUtc) {
        element.textContent = "Awaiting first sync…";
        return;
    }
    element.textContent = `Last sync ${formatEventTime(snapshot.lastSyncedAtUtc)}`;
}
function updateSectionIndicators(cards, snapshot) {
    cards.forEach((refs, sectionId) => {
        const dirtyState = snapshot.dirtySections[sectionId];
        if (refs.dirtyBadge) {
            const badge = refs.dirtyBadge;
            if (dirtyState) {
                badge.hidden = false;
                badge.title = describeDiffShort(dirtyState.diff);
            }
            else {
                badge.hidden = true;
                badge.removeAttribute("title");
            }
        }
        if (refs.conflictBadge) {
            refs.conflictBadge.hidden = !snapshot.conflictSectionIds.includes(sectionId);
        }
        if (refs.reviewBadge && refs.reviewLabel) {
            const status = snapshot.reviewStatuses[sectionId];
            if (status) {
                refs.reviewBadge.hidden = false;
                refs.reviewBadge.dataset.status = status;
                refs.reviewLabel.textContent = REVIEW_LABEL[status] ?? status;
            }
            else {
                refs.reviewBadge.hidden = true;
                refs.reviewBadge.removeAttribute("data-status");
            }
        }
    });
}
function formatEventTime(timestamp) {
    const date = new Date(timestamp);
    if (Number.isNaN(date.getTime())) {
        return timestamp;
    }
    return date.toLocaleString(undefined, {
        hour: "2-digit",
        minute: "2-digit",
        second: "2-digit"
    });
}
function describeDiffShort(diff) {
    if (!diff) {
        return "Draft updated";
    }
    const change = diff.changeType.charAt(0).toUpperCase() + diff.changeType.slice(1);
    const key = diff.after?.payload.sectionKey ??
        diff.before?.payload.sectionKey ??
        diff.sectionId;
    return `${change} · ${key}`;
}
function formatChangeType(change) {
    return change.charAt(0).toUpperCase() + change.slice(1);
}
