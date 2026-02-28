import { fetchSections } from "@bugence/core";
import { getDashboardHistory, getDashboardState, loadDashboardHistory, loadDashboardPages } from "../store/dashboardStore";
const FRESH_THRESHOLD_HOURS = 48;
const STALE_THRESHOLD_DAYS = 14;
const DATE_FULL = new Intl.DateTimeFormat(undefined, {
    month: "short",
    day: "2-digit",
    year: "numeric"
});
const DATE_TIME_SHORT = new Intl.DateTimeFormat(undefined, {
    month: "short",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit"
});
const TIME_ONLY = new Intl.DateTimeFormat(undefined, {
    hour: "2-digit",
    minute: "2-digit"
});
export function initLibraryConsole() {
    const root = document.querySelector("[data-library-console]");
    if (!root) {
        return;
    }
    const metricsRefs = {
        totalPages: root.querySelector('[data-library-metric="totalPages"]'),
        totalSections: root.querySelector('[data-library-metric="totalSections"]'),
        activePastWeek: root.querySelector('[data-library-metric="activePastWeek"]'),
        latestUpdate: root.querySelector('[data-library-metric="latestUpdate"]')
    };
    const badgeCount = root.querySelector("[data-library-count]");
    const pageGrid = root.querySelector("[data-library-page-list]");
    const emptyState = root.querySelector("[data-library-empty]");
    const trendingHost = root.querySelector("[data-library-trending] .library-console__chip-stack");
    const controlsHost = root.querySelector("[data-library-controls]");
    const searchInput = root.querySelector("#term") ?? document.getElementById("term");
    const statusRefs = Array.from(root.querySelectorAll("[data-library-status]")).map((element) => ({
        root: element,
        kind: (element.dataset.statusKind ?? "").trim(),
        value: element.querySelector("strong"),
        meter: element.querySelector(".library-console__status-meter span")
    }));
    const inspectorRoot = document.querySelector("[data-library-inspector]");
    const inspector = inspectorRoot
        ? createInspector(inspectorRoot, {
            loadSections: createSectionsLoader(),
            loadActivity: createActivityLoader()
        })
        : null;
    const state = {
        pages: pageGrid ? extractPagesFromDom(pageGrid) : [],
        sort: "recent",
        filter: "all",
        search: (searchInput?.value ?? "").trim()
    };
    if (!pageGrid) {
        console.warn("[libraryConsole] Page grid container not found, skipping enhancements.");
        return;
    }
    const applyRender = () => renderLibrary({
        root,
        state,
        pageGrid,
        emptyState,
        badgeCount,
        metricsRefs,
        statusRefs,
        trendingHost
    });
    applyRender();
    void loadDashboardPages({ force: true, ttlMs: 60_000 })
        .then(() => {
        const store = getDashboardState();
        state.pages = [...store.pages];
        applyRender();
    })
        .catch((error) => {
        console.warn("[libraryConsole] Unable to refresh pages", error);
    });
    if (searchInput) {
        searchInput.addEventListener("input", () => {
            state.search = searchInput.value.trim();
            applyRender();
        });
    }
    root.addEventListener("click", (event) => {
        const target = event.target;
        if (!target) {
            return;
        }
        const chip = target.closest("[data-library-chip]");
        if (chip && searchInput) {
            event.preventDefault();
            const term = chip.dataset.libraryTerm ?? chip.textContent?.trim() ?? "";
            searchInput.value = term;
            state.search = term;
            applyRender();
            return;
        }
        const inspectButton = target.closest("[data-library-inspect]");
        if (inspectButton && inspector) {
            event.preventDefault();
            const pageId = inspectButton.getAttribute("data-library-inspect");
            if (!pageId) {
                return;
            }
            const page = state.pages.find((entry) => entry.id === pageId);
            if (!page) {
                return;
            }
            inspector.open(page, inspectButton);
        }
    });
    if (controlsHost) {
        updateActiveControl(controlsHost, "[data-library-sort]", state.sort);
        updateActiveControl(controlsHost, "[data-library-filter]", state.filter);
        controlsHost.addEventListener("click", (event) => {
            const target = event.target;
            if (!target) {
                return;
            }
            const sortButton = target.closest("[data-library-sort]");
            if (sortButton) {
                const sortKey = sortButton.getAttribute("data-library-sort");
                if (sortKey && state.sort !== sortKey) {
                    state.sort = sortKey;
                    updateActiveControl(controlsHost, "[data-library-sort]", sortKey);
                    applyRender();
                }
                return;
            }
            const filterButton = target.closest("[data-library-filter]");
            if (filterButton) {
                const filterKey = filterButton.getAttribute("data-library-filter");
                if (filterKey && state.filter !== filterKey) {
                    state.filter = filterKey;
                    updateActiveControl(controlsHost, "[data-library-filter]", filterKey);
                    applyRender();
                }
            }
        });
    }
}
function renderLibrary(context) {
    const { state, pageGrid, emptyState, badgeCount, metricsRefs, statusRefs, trendingHost } = context;
    const pages = [...state.pages];
    const totalCount = pages.length;
    const filtered = pages
        .filter((page) => filterPage(page, state.filter))
        .filter((page) => matchesSearch(page, state.search));
    sortPages(filtered, state.sort);
    renderPageCards(pageGrid, filtered);
    if (emptyState) {
        emptyState.hidden = filtered.length > 0;
    }
    pageGrid.hidden = filtered.length === 0;
    if (badgeCount) {
        badgeCount.textContent =
            filtered.length === totalCount
                ? totalCount.toString()
                : `${filtered.length} / ${totalCount}`;
    }
    updateHeroMetrics(metricsRefs, pages);
    updateStatusMetrics(statusRefs, pages);
    renderTrending(trendingHost, pages);
}
function filterPage(page, filter) {
    if (filter === "all") {
        return true;
    }
    const updatedAt = new Date(page.updatedAtUtc);
    const lastPublished = page.lastPublishedAtUtc ? new Date(page.lastPublishedAtUtc) : null;
    switch (filter) {
        case "fresh":
            return hoursSince(updatedAt) <= FRESH_THRESHOLD_HOURS;
        case "stale":
            return hoursSince(updatedAt) >= STALE_THRESHOLD_DAYS * 24;
        case "unpublished":
            return !lastPublished;
        default:
            return true;
    }
}
function matchesSearch(page, search) {
    if (!search) {
        return true;
    }
    const haystack = `${page.name ?? ""} ${page.slug ?? ""} ${page.description ?? ""}`.toLowerCase();
    return haystack.includes(search.toLowerCase());
}
function sortPages(pages, sort) {
    switch (sort) {
        case "sections":
            pages.sort((a, b) => b.sectionCount - a.sectionCount || a.name.localeCompare(b.name));
            break;
        case "alphabetical":
            pages.sort((a, b) => a.name.localeCompare(b.name));
            break;
        case "recent":
        default:
            pages.sort((a, b) => new Date(b.updatedAtUtc).getTime() - new Date(a.updatedAtUtc).getTime());
            break;
    }
}
function renderPageCards(container, pages) {
    container.innerHTML = "";
    if (!pages.length) {
        return;
    }
    const fragment = document.createDocumentFragment();
    pages.forEach((page) => {
        fragment.appendChild(createPageCard(page));
    });
    container.appendChild(fragment);
}
function createPageCard(page) {
    const article = document.createElement("article");
    article.className = "library-console__page-card";
    article.dataset.pageId = page.id;
    article.dataset.pageName = page.name;
    article.dataset.pageSlug = page.slug;
    if (page.description) {
        article.dataset.pageDescription = page.description;
    }
    article.dataset.pageUpdated = page.updatedAtUtc;
    if (page.lastPublishedAtUtc) {
        article.dataset.pagePublished = page.lastPublishedAtUtc;
    }
    article.dataset.pageSections = String(page.sectionCount);
    article.dataset.pageText = String(page.textSections);
    article.dataset.pageImages = String(page.imageSections);
    const header = document.createElement("header");
    header.className = "library-console__page-header";
    const slug = document.createElement("span");
    slug.className = "library-console__page-slug";
    slug.textContent = page.slug;
    const title = document.createElement("h3");
    title.textContent = page.name;
    const description = document.createElement("p");
    description.textContent = page.description ?? "No description yet.";
    header.append(slug, title, description);
    const metrics = document.createElement("dl");
    metrics.className = "library-console__page-metrics";
    metrics.append(createMetricEntry("Sections", page.sectionCount), createMetricEntry("Text frames", page.textSections), createMetricEntry("Imagery slots", page.imageSections));
    const footer = document.createElement("footer");
    footer.className = "library-console__page-footer";
    const meta = document.createElement("div");
    const updatedLabel = document.createElement("span");
    updatedLabel.className = "library-console__timestamp-label";
    updatedLabel.textContent = "Updated";
    const updatedTime = document.createElement("time");
    updatedTime.className = "library-console__timestamp";
    updatedTime.dateTime = page.updatedAtUtc;
    updatedTime.textContent = formatFullDate(page.updatedAtUtc);
    const updatedSecondary = document.createElement("span");
    updatedSecondary.className = "library-console__timestamp-secondary";
    updatedSecondary.textContent = `${formatTimeOnly(page.updatedAtUtc)} local`;
    const publishedLabel = document.createElement("span");
    publishedLabel.className = "library-console__timestamp-secondary library-console__timestamp-secondary--published";
    publishedLabel.textContent = `Last publish: ${page.lastPublishedAtUtc ? formatFullDate(page.lastPublishedAtUtc) : "Draft only"}`;
    meta.append(updatedLabel, updatedTime, updatedSecondary, publishedLabel);
    const actions = document.createElement("div");
    actions.className = "library-console__page-actions";
    actions.append(createActionLink(`/content/canvas/${page.id}`, "library-console__action", "fa-solid fa-pen-to-square", "Edit", true), createActionLink(`/Content/LivePreview?pageId=${encodeURIComponent(page.id)}`, "library-console__action library-console__action--ghost", "fa-solid fa-eye", "Preview"), createDetailButton(page.id));
    footer.append(meta, actions);
    article.append(header, metrics, footer);
    return article;
}
function createMetricEntry(label, value) {
    const wrapper = document.createElement("div");
    const dt = document.createElement("dt");
    dt.textContent = label;
    const dd = document.createElement("dd");
    dd.textContent = value.toString();
    wrapper.append(dt, dd);
    return wrapper;
}
function createActionLink(href, className, iconClass, label, external = false) {
    const anchor = document.createElement("a");
    anchor.href = href;
    anchor.className = className;
    if (external) {
        anchor.target = "_blank";
        anchor.rel = "noopener";
    }
    const icon = document.createElement("span");
    icon.className = iconClass;
    anchor.append(icon, document.createTextNode(label));
    return anchor;
}
function createDetailButton(pageId) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "library-console__action library-console__action--detail";
    button.setAttribute("data-library-inspect", pageId);
    const icon = document.createElement("span");
    icon.className = "fa-solid fa-circle-info";
    const label = document.createTextNode("Details");
    button.append(icon, label);
    return button;
}
function updateHeroMetrics(refs, pages) {
    const totals = pages.reduce((acc, page) => {
        acc.sectionCount += page.sectionCount;
        acc.textSections += page.textSections;
        acc.imageSections += page.imageSections;
        if (page.updatedAtUtc > acc.latestUpdateUtc) {
            acc.latestUpdateUtc = page.updatedAtUtc;
        }
        if (hoursSince(new Date(page.updatedAtUtc)) <= 7 * 24) {
            acc.activePastWeek += 1;
        }
        return acc;
    }, {
        pageCount: pages.length,
        sectionCount: 0,
        textSections: 0,
        imageSections: 0,
        activePastWeek: 0,
        latestUpdateUtc: ""
    });
    if (refs.totalPages) {
        refs.totalPages.textContent = totals.pageCount.toString();
    }
    if (refs.totalSections) {
        refs.totalSections.textContent = totals.sectionCount.toString();
    }
    if (refs.activePastWeek) {
        refs.activePastWeek.textContent = totals.activePastWeek.toString();
    }
    if (refs.latestUpdate) {
        if (totals.latestUpdateUtc) {
            refs.latestUpdate.dataset.libraryLatest = totals.latestUpdateUtc;
            refs.latestUpdate.textContent = `${formatFullDate(totals.latestUpdateUtc)} - ${formatTimeOnly(totals.latestUpdateUtc)}`;
        }
        else {
            refs.latestUpdate.dataset.libraryLatest = "";
            refs.latestUpdate.textContent = "Awaiting first publish";
        }
    }
}
function updateStatusMetrics(refs, pages) {
    const counts = pages.reduce((acc, page) => {
        if (page.sectionCount > 0) {
            acc["Launch ready"] = (acc["Launch ready"] ?? 0) + 1;
        }
        if (hoursSince(new Date(page.updatedAtUtc)) <= FRESH_THRESHOLD_HOURS) {
            acc["In review"] = (acc["In review"] ?? 0) + 1;
        }
        if ((page.description ?? "").toLowerCase().includes("test")) {
            acc["Experimenting"] = (acc["Experimenting"] ?? 0) + 1;
        }
        return acc;
    }, {});
    refs.forEach(({ root, value, meter, kind }) => {
        const total = pages.length || 1;
        const nextValue = counts[kind] ?? 0;
        if (value) {
            value.textContent = nextValue.toString();
        }
        if (meter) {
            const ratio = Math.max(0, Math.min(1, nextValue / total));
            meter.style.width = `${Math.round(ratio * 100)}%`;
        }
        root.dataset.state = nextValue > 0 ? "active" : "idle";
    });
}
function renderTrending(host, pages) {
    if (!host) {
        return;
    }
    const top = [...pages]
        .sort((a, b) => new Date(b.updatedAtUtc).getTime() - new Date(a.updatedAtUtc).getTime())
        .slice(0, 3);
    host.innerHTML = "";
    if (!top.length) {
        return;
    }
    top.forEach((page) => {
        const anchor = document.createElement("a");
        anchor.href = `/Content/Library?term=${encodeURIComponent(page.name)}`;
        anchor.className = "library-console__chip";
        anchor.dataset.libraryChip = "";
        anchor.dataset.libraryTerm = page.name;
        anchor.dataset.pageId = page.id;
        anchor.dataset.pageUpdated = page.updatedAtUtc;
        const icon = document.createElement("span");
        icon.className = "fa-solid fa-sparkles";
        icon.setAttribute("aria-hidden", "true");
        const label = document.createTextNode(page.name);
        const meta = document.createElement("span");
        meta.className = "library-console__chip-meta";
        meta.textContent = `${formatDateTimeShort(page.updatedAtUtc)}`;
        anchor.append(icon, label, meta);
        host.appendChild(anchor);
    });
}
function updateActiveControl(host, selector, value) {
    host.querySelectorAll(selector).forEach((element) => {
        const key = element.getAttribute(selector.replace(/[\[\]]/g, "").split("=")[0]) ?? "";
        if (key === value) {
            element.dataset.state = "active";
        }
        else {
            element.dataset.state = "idle";
        }
    });
}
function extractPagesFromDom(container) {
    const items = [];
    container.querySelectorAll("[data-page-id]").forEach((node) => {
        const updated = node.dataset.pageUpdated;
        if (!updated) {
            return;
        }
        items.push({
            id: node.dataset.pageId ?? "",
            name: node.dataset.pageName ?? "",
            slug: node.dataset.pageSlug ?? "",
            description: node.dataset.pageDescription ?? "",
            updatedAtUtc: updated,
            lastPublishedAtUtc: node.dataset.pagePublished ?? null,
            sectionCount: Number.parseInt(node.dataset.pageSections ?? "0", 10) || 0,
            textSections: Number.parseInt(node.dataset.pageText ?? "0", 10) || 0,
            imageSections: Number.parseInt(node.dataset.pageImages ?? "0", 10) || 0
        });
    });
    return items;
}
function hoursSince(date) {
    return (Date.now() - date.getTime()) / 36e5;
}
function formatFullDate(value) {
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? value : DATE_FULL.format(date);
}
function formatDateTimeShort(value) {
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? value : DATE_TIME_SHORT.format(date);
}
function formatTimeOnly(value) {
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? value : TIME_ONLY.format(date);
}
function createSectionsLoader() {
    const cache = new Map();
    return async (pageId) => {
        if (cache.has(pageId)) {
            return cache.get(pageId) ?? [];
        }
        try {
            const result = await fetchSections(pageId, { revalidate: true, ttlMs: 0 });
            const sections = (result.data.sections ?? []).map(mapSectionForInspector);
            cache.set(pageId, sections);
            return sections;
        }
        catch (error) {
            console.warn("[libraryConsole] Unable to load sections for inspector", error);
            cache.set(pageId, []);
            return [];
        }
    };
}
function createActivityLoader() {
    const cache = new Map();
    return async (pageId) => {
        if (cache.has(pageId)) {
            return cache.get(pageId) ?? [];
        }
        try {
            await loadDashboardHistory({ pageId, take: 8, force: true, ttlMs: 0 });
            const history = getDashboardHistory(pageId, 8);
            cache.set(pageId, history);
            return history;
        }
        catch (error) {
            console.warn("[libraryConsole] Unable to load history for inspector", error);
            cache.set(pageId, []);
            return [];
        }
    };
}
function mapSectionForInspector(section) {
    return {
        id: section.id,
        sectionKey: section.sectionKey,
        title: section.title,
        contentType: section.contentType,
        updatedAtUtc: section.updatedAtUtc,
        lastPublishedAtUtc: section.lastPublishedAtUtc ?? null
    };
}
function createInspector(root, services) {
    const slugEl = root.querySelector("[data-library-inspector-slug]");
    const titleEl = root.querySelector("[data-library-inspector-title]");
    const descriptionEl = root.querySelector("[data-library-inspector-description]");
    const sectionsEl = root.querySelector("[data-library-inspector-sections]");
    const textEl = root.querySelector("[data-library-inspector-text]");
    const imagesEl = root.querySelector("[data-library-inspector-images]");
    const signalsList = root.querySelector("[data-library-inspector-signals]");
    const sectionList = root.querySelector("[data-library-inspector-section-list]");
    const activityList = root.querySelector("[data-library-inspector-activity]");
    const tabs = Array.from(root.querySelectorAll("[data-library-inspector-tabs] [data-tab]"));
    const panels = Array.from(root.querySelectorAll("[data-library-inspector-panels] [data-panel]"));
    const closers = root.querySelectorAll("[data-library-close]");
    let lastTrigger = null;
    let activeTab = "summary";
    tabs.forEach((tab) => {
        tab.addEventListener("click", () => {
            const next = tab.getAttribute("data-tab");
            if (!next || next === activeTab) {
                return;
            }
            activeTab = next;
            updateInspectorTabs(tabs, panels, activeTab);
        });
    });
    closers.forEach((closer) => {
        closer.addEventListener("click", () => hide());
    });
    root.addEventListener("keydown", (event) => {
        if (event.key === "Escape") {
            hide();
        }
    });
    function open(page, trigger) {
        lastTrigger = trigger;
        activeTab = "summary";
        updateInspectorTabs(tabs, panels, activeTab);
        renderSummary(page);
        void renderSections(page.id);
        void renderActivity(page.id);
        root.hidden = false;
        requestAnimationFrame(() => {
            root.dataset.state = "open";
            const closeButton = root.querySelector(".library-inspector__close");
            closeButton?.focus({ preventScroll: true });
            document.body.style.setProperty("overflow", "hidden");
        });
    }
    function hide() {
        root.dataset.state = "idle";
        document.body.style.removeProperty("overflow");
        setTimeout(() => {
            root.hidden = true;
            if (lastTrigger) {
                lastTrigger.focus({ preventScroll: true });
            }
        }, 180);
    }
    function renderSummary(page) {
        if (slugEl)
            slugEl.textContent = page.slug ?? "";
        if (titleEl)
            titleEl.textContent = page.name ?? "";
        if (descriptionEl) {
            descriptionEl.textContent = page.description ?? "No description captured yet.";
        }
        if (sectionsEl)
            sectionsEl.textContent = page.sectionCount.toString();
        if (textEl)
            textEl.textContent = page.textSections.toString();
        if (imagesEl)
            imagesEl.textContent = page.imageSections.toString();
        if (signalsList) {
            signalsList.innerHTML = "";
            const signals = buildSummarySignals(page);
            signals.forEach((signal) => {
                const item = document.createElement("li");
                item.textContent = signal;
                signalsList.appendChild(item);
            });
        }
    }
    async function renderSections(pageId) {
        if (!sectionList)
            return;
        sectionList.innerHTML = "";
        const loader = document.createElement("li");
        loader.className = "library-inspector__loading";
        loader.textContent = "Loading sections...";
        sectionList.appendChild(loader);
        const sections = await services.loadSections(pageId);
        sectionList.innerHTML = "";
        if (!sections.length) {
            const empty = document.createElement("li");
            empty.className = "library-inspector__empty";
            empty.textContent = "No sections available yet.";
            sectionList.appendChild(empty);
            return;
        }
        sections.forEach((section) => {
            const item = document.createElement("li");
            item.className = "library-inspector__list-item";
            const header = document.createElement("div");
            header.className = "library-inspector__list-head";
            const title = document.createElement("strong");
            title.textContent = section.title ?? section.sectionKey ?? "Untitled section";
            const meta = document.createElement("span");
            meta.textContent = `${section.contentType} - Updated ${formatDateTimeShort(section.updatedAtUtc)}`;
            header.append(title, meta);
            const footer = document.createElement("div");
            footer.className = "library-inspector__list-meta";
            footer.textContent = section.lastPublishedAtUtc
                ? `Last publish ${formatDateTimeShort(section.lastPublishedAtUtc)}`
                : "Draft not yet published";
            item.append(header, footer);
            sectionList.appendChild(item);
        });
    }
    async function renderActivity(pageId) {
        if (!activityList)
            return;
        activityList.innerHTML = "";
        const loader = document.createElement("li");
        loader.className = "library-inspector__loading";
        loader.textContent = "Loading activity...";
        activityList.appendChild(loader);
        const history = await services.loadActivity(pageId);
        activityList.innerHTML = "";
        if (!history.length) {
            const empty = document.createElement("li");
            empty.className = "library-inspector__empty";
            empty.textContent = "No recent edits recorded.";
            activityList.appendChild(empty);
            return;
        }
        history.forEach((entry) => {
            const item = document.createElement("li");
            item.className = "library-inspector__list-item";
            const header = document.createElement("div");
            header.className = "library-inspector__list-head";
            const title = document.createElement("strong");
            title.textContent = entry.changeSummary ?? entry.fieldKey ?? "Content update";
            const meta = document.createElement("span");
            meta.textContent = `${entry.performedByDisplayName ?? "Unknown"} - ${formatDateTimeShort(entry.performedAtUtc)}`;
            header.append(title, meta);
            const footer = document.createElement("div");
            footer.className = "library-inspector__list-meta";
            footer.textContent = buildHistorySnippet(entry);
            item.append(header, footer);
            activityList.appendChild(item);
        });
    }
    return { open, hide };
}
function updateInspectorTabs(tabs, panels, active) {
    tabs.forEach((tab) => {
        const isActive = tab.getAttribute("data-tab") === active;
        tab.dataset.state = isActive ? "active" : "idle";
    });
    panels.forEach((panel) => {
        panel.dataset.state = panel.getAttribute("data-panel") === active ? "active" : "idle";
    });
}
function buildSummarySignals(page) {
    const signals = [];
    const updated = formatDateTimeShort(page.updatedAtUtc);
    signals.push(`Updated ${updated}`);
    if (page.lastPublishedAtUtc) {
        signals.push(`Last publish ${formatDateTimeShort(page.lastPublishedAtUtc)}`);
        if (new Date(page.updatedAtUtc).getTime() > new Date(page.lastPublishedAtUtc).getTime()) {
            signals.push("Draft changes pending publish");
        }
    }
    else {
        signals.push("Draft has not been published yet");
    }
    const textRatio = page.sectionCount > 0 ? Math.round((page.textSections / page.sectionCount) * 100) : 0;
    signals.push(`Copy density ${textRatio}% text - ${page.imageSections} imagery slots`);
    return signals;
}
function buildHistorySnippet(entry) {
    if (entry.changeSummary) {
        return entry.changeSummary;
    }
    const previous = entry.previousValue ?? "";
    const next = entry.newValue ?? "";
    if (previous && next && previous !== next) {
        return `Updated ${truncate(next)}`;
    }
    if (!previous && next) {
        return `Added ${truncate(next)}`;
    }
    if (previous && !next) {
        return `Removed ${truncate(previous)}`;
    }
    return "No change captured.";
}
function truncate(value, length = 60) {
    const trimmed = value.replace(/\s+/g, " ").trim();
    return trimmed.length > length ? `${trimmed.slice(0, length)}...` : trimmed;
}

