import { getTimelineSnapshot, useDashboardStore } from "../store/dashboardStore";
export function initDashboardNotifications() {
    const host = document.querySelector("[data-dashboard-notifications]");
    const pageId = document.querySelector("[data-dashboard-page]")?.getAttribute("data-dashboard-page");
    if (!host || !pageId) {
        return;
    }
    const store = useDashboardStore();
    const seen = new Set();
    const handleSnapshot = (snapshot) => {
        snapshot.events.forEach((event) => {
            if (seen.has(event.id)) {
                return;
            }
            seen.add(event.id);
            switch (event.type) {
                case "draft-conflict":
                    pushNotification(host, "Conflict detected", event.description ?? "Resolve conflicts before publishing.");
                    dispatchTask(`conflict-${event.sectionId}`, `Resolve conflict for ${event.sectionKey ?? "a section"}`);
                    break;
                case "review-status":
                    if (event.reviewStatus === "pending") {
                        pushNotification(host, "Review requested", `${event.sectionKey ?? "Section"} needs review.`);
                        dispatchTask(`review-${event.sectionId}`, `Complete review for ${event.sectionKey ?? "section"}`);
                    }
                    if (event.reviewStatus === "rejected") {
                        pushNotification(host, "Review rejected", event.description ?? "Update content before publishing.");
                        dispatchTask(`rework-${event.sectionId}`, `Address review feedback for ${event.sectionKey ?? "section"}`);
                    }
                    break;
                case "draft-dirty":
                    pushNotification(host, "Draft updated", event.description ?? "New draft changes ready for review.");
                    break;
                case "publish-summary":
                    pushNotification(host, "Publish ready", "Diff summary prepared in publish console.");
                    break;
                default:
                    break;
            }
        });
    };
    const render = () => {
        const snapshot = getTimelineSnapshot(pageId);
        handleSnapshot(snapshot);
    };
    render();
    const unsubscribe = store.subscribe(render);
    window.addEventListener("beforeunload", () => {
        unsubscribe();
    }, { once: true });
}
function pushNotification(host, title, message) {
    const item = document.createElement("div");
    item.className = "dashboard-notification";
    const heading = document.createElement("strong");
    heading.textContent = title;
    const body = document.createElement("p");
    body.textContent = message;
    item.append(heading, body);
    host.appendChild(item);
    requestAnimationFrame(() => {
        item.dataset.state = "visible";
    });
    setTimeout(() => {
        item.dataset.state = "closing";
        setTimeout(() => {
            item.remove();
        }, 300);
    }, 4500);
}
function dispatchTask(id, label) {
    document.dispatchEvent(new CustomEvent("dashboard:task:add", { detail: { id, label } }));
}
