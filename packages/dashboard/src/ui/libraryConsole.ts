import {
  fetchSections,
  type ContentHistoryEntry,
  type ContentPageListItem,
  type IsoDateString,
  type PageSectionWithHistory
} from "@bugence/core";
import {
  getDashboardHistory,
  getDashboardState,
  loadDashboardHistory,
  loadDashboardPages
} from "../store/dashboardStore";

type SortKey = "recent" | "sections" | "alphabetical";
type FilterKey = "all" | "fresh" | "stale" | "unpublished";

interface LibraryState {
  pages: ContentPageListItem[];
  sort: SortKey;
  filter: FilterKey;
  search: string;
}

interface LibraryMetricsRefs {
  totalPages?: HTMLElement | null;
  totalSections?: HTMLElement | null;
  activePastWeek?: HTMLElement | null;
  latestUpdate?: HTMLElement | null;
}

interface StatusMetricRefs {
  root: HTMLElement;
  value: HTMLElement | null;
  meter: HTMLElement | null;
  kind: string;
}

interface InspectorSection {
  id: string;
  sectionKey?: string | null;
  title?: string | null;
  contentType: string;
  updatedAtUtc: IsoDateString;
  lastPublishedAtUtc?: IsoDateString | null;
}

interface InspectorServices {
  loadSections(pageId: string): Promise<InspectorSection[]>;
  loadActivity(pageId: string): Promise<ContentHistoryEntry[]>;
}

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
  const root = document.querySelector<HTMLElement>("[data-library-console]");
  if (!root) {
    return;
  }

  const metricsRefs: LibraryMetricsRefs = {
    totalPages: root.querySelector<HTMLElement>('[data-library-metric="totalPages"]'),
    totalSections: root.querySelector<HTMLElement>('[data-library-metric="totalSections"]'),
    activePastWeek: root.querySelector<HTMLElement>('[data-library-metric="activePastWeek"]'),
    latestUpdate: root.querySelector<HTMLElement>('[data-library-metric="latestUpdate"]')
  };

  const badgeCount = root.querySelector<HTMLElement>("[data-library-count]");
  const pageGrid = root.querySelector<HTMLElement>("[data-library-page-list]");
  const emptyState = root.querySelector<HTMLElement>("[data-library-empty]");
  const trendingHost = root.querySelector<HTMLElement>("[data-library-trending] .library-console__chip-stack");
  const controlsHost = root.querySelector<HTMLElement>("[data-library-controls]");
  const searchInput =
    root.querySelector<HTMLInputElement>("#term") ?? document.getElementById("term") as HTMLInputElement | null;

  const statusRefs: StatusMetricRefs[] = Array.from(
    root.querySelectorAll<HTMLElement>("[data-library-status]")
  ).map((element) => ({
    root: element,
    kind: (element.dataset.statusKind ?? "").trim(),
    value: element.querySelector<HTMLElement>("strong"),
    meter: element.querySelector<HTMLElement>(".library-console__status-meter span")
  }));

  const inspectorRoot = document.querySelector<HTMLElement>("[data-library-inspector]");
  const inspector = inspectorRoot
    ? createInspector(inspectorRoot, {
        loadSections: createSectionsLoader(),
        loadActivity: createActivityLoader()
      })
    : null;

  const state: LibraryState = {
    pages: pageGrid ? extractPagesFromDom(pageGrid) : [],
    sort: "recent",
    filter: "all",
    search: (searchInput?.value ?? "").trim()
  };

  state.pages = state.pages.map((page) => normalizePageItem(page));

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
      state.pages = store.pages.map((page) => normalizePageItem(page));
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
    const target = event.target as HTMLElement | null;
    if (!target) {
      return;
    }

    const chip = target.closest<HTMLElement>("[data-library-chip]");
    if (chip && searchInput) {
      event.preventDefault();
      const term = chip.dataset.libraryTerm ?? chip.textContent?.trim() ?? "";
      searchInput.value = term;
      state.search = term;
      applyRender();
      return;
    }

    const inspectButton = target.closest<HTMLElement>("[data-library-inspect]");
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
      const target = event.target as HTMLElement | null;
      if (!target) {
        return;
      }

      const sortButton = target.closest<HTMLElement>("[data-library-sort]");
      if (sortButton) {
        const sortKey = sortButton.getAttribute("data-library-sort") as SortKey;
        if (sortKey && state.sort !== sortKey) {
          state.sort = sortKey;
          updateActiveControl(controlsHost, "[data-library-sort]", sortKey);
          applyRender();
        }
        return;
      }

      const filterButton = target.closest<HTMLElement>("[data-library-filter]");
      if (filterButton) {
        const filterKey = filterButton.getAttribute("data-library-filter") as FilterKey;
        if (filterKey && state.filter !== filterKey) {
          state.filter = filterKey;
          updateActiveControl(controlsHost, "[data-library-filter]", filterKey);
          applyRender();
        }
      }
    });
  }
}

function renderLibrary(context: {
  root: HTMLElement;
  state: LibraryState;
  pageGrid: HTMLElement;
  emptyState: HTMLElement | null;
  badgeCount: HTMLElement | null;
  metricsRefs: LibraryMetricsRefs;
  statusRefs: StatusMetricRefs[];
  trendingHost: HTMLElement | null;
  }) {
    const { state, pageGrid, emptyState, badgeCount, metricsRefs, statusRefs, trendingHost } = context;
    const pages = state.pages.map((page) => normalizePageItem(page));
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

function filterPage(page: ContentPageListItem, filter: FilterKey): boolean {
  if (filter === "all") {
    return true;
  }

    const updatedAt = new Date(page.updatedAtUtc);
    const hasValidUpdatedAt = !Number.isNaN(updatedAt.getTime());
    const lastPublished = page.lastPublishedAtUtc ? new Date(page.lastPublishedAtUtc) : null;
    const hasValidPublishedAt = lastPublished ? !Number.isNaN(lastPublished.getTime()) : false;

    switch (filter) {
      case "fresh":
        return hasValidUpdatedAt && hoursSince(updatedAt) <= FRESH_THRESHOLD_HOURS;
      case "stale":
        return hasValidUpdatedAt && hoursSince(updatedAt) >= STALE_THRESHOLD_DAYS * 24;
      case "unpublished":
        return !hasValidPublishedAt;
      default:
        return true;
    }
  }

function matchesSearch(page: ContentPageListItem, search: string): boolean {
  if (!search) {
    return true;
  }

  const haystack = `${page.name ?? ""} ${page.slug ?? ""} ${page.description ?? ""}`.toLowerCase();
  return haystack.includes(search.toLowerCase());
}

function sortPages(pages: ContentPageListItem[], sort: SortKey) {
  switch (sort) {
    case "sections":
      pages.sort((a, b) => b.sectionCount - a.sectionCount || a.name.localeCompare(b.name));
      break;
    case "alphabetical":
      pages.sort((a, b) => a.name.localeCompare(b.name));
      break;
    case "recent":
    default:
      pages.sort(
        (a, b) =>
          new Date(b.updatedAtUtc).getTime() - new Date(a.updatedAtUtc).getTime()
      );
      break;
  }
}

function renderPageCards(container: HTMLElement, pages: ContentPageListItem[]) {
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

function createPageCard(page: ContentPageListItem): HTMLElement {
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
  metrics.append(
    createMetricEntry("Sections", page.sectionCount),
    createMetricEntry("Text frames", page.textSections),
    createMetricEntry("Imagery slots", page.imageSections)
  );

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

  actions.append(
    createActionLink(`/content/canvas/${page.id}`, "library-console__action", "fa-solid fa-pen-to-square", "Edit", true),
    createActionLink(`/Content/LivePreview?pageId=${encodeURIComponent(page.id)}`, "library-console__action library-console__action--ghost", "fa-solid fa-eye", "Preview"),
    createDetailButton(page.id)
  );

  footer.append(meta, actions);
  article.append(header, metrics, footer);
  return article;
}

function createMetricEntry(label: string, value: number): HTMLElement {
  const wrapper = document.createElement("div");
  const dt = document.createElement("dt");
  dt.textContent = label;
  const dd = document.createElement("dd");
  dd.textContent = value.toString();
  wrapper.append(dt, dd);
  return wrapper;
}

function createActionLink(
  href: string,
  className: string,
  iconClass: string,
  label: string,
  external = false
): HTMLElement {
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

function createDetailButton(pageId: string): HTMLElement {
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

function updateHeroMetrics(refs: LibraryMetricsRefs, pages: ContentPageListItem[]) {
  const totals = pages.reduce(
    (acc, page) => {
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
    },
    {
      pageCount: pages.length,
      sectionCount: 0,
      textSections: 0,
      imageSections: 0,
      activePastWeek: 0,
      latestUpdateUtc: ""
    }
  );

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
      refs.latestUpdate.textContent = `${formatFullDate(totals.latestUpdateUtc)} · ${formatTimeOnly(totals.latestUpdateUtc)}`;
    } else {
      refs.latestUpdate.dataset.libraryLatest = "";
      refs.latestUpdate.textContent = "Awaiting first publish";
    }
  }
}

function updateStatusMetrics(refs: StatusMetricRefs[], pages: ContentPageListItem[]) {
  const counts = pages.reduce<Record<string, number>>((acc, page) => {
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

function renderTrending(host: HTMLElement | null, pages: ContentPageListItem[]) {
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

function updateActiveControl(host: HTMLElement, selector: string, value: string) {
  host.querySelectorAll<HTMLElement>(selector).forEach((element) => {
    const key = element.getAttribute(selector.replace(/[\[\]]/g, "").split("=")[0]) ?? "";
    if (key === value) {
      element.dataset.state = "active";
    } else {
      element.dataset.state = "idle";
    }
  });
}

function extractPagesFromDom(container: HTMLElement): ContentPageListItem[] {
  const items: ContentPageListItem[] = [];

  container.querySelectorAll<HTMLElement>("[data-page-id]").forEach((node) => {
    const raw: Record<string, unknown> = {
      id: node.dataset.pageId ?? "",
      name: node.dataset.pageName ?? node.querySelector("h3")?.textContent ?? "",
      slug:
        node.dataset.pageSlug ??
        node.querySelector("[data-page-slug], .library-console__page-slug")?.textContent ??
        "",
      description:
        node.dataset.pageDescription ??
        node.querySelector("[data-page-description], p")?.textContent ??
        "",
      updatedAtUtc: node.dataset.pageUpdated ?? "",
      lastPublishedAtUtc: node.dataset.pagePublished && node.dataset.pagePublished.trim().length > 0
        ? node.dataset.pagePublished
        : null,
      sectionCount: node.dataset.pageSections,
      textSections: node.dataset.pageText,
      imageSections: node.dataset.pageImages
    };

    items.push(normalizePageItem(raw));
  });

  return items;
}

function normalizePageItem(raw: ContentPageListItem | Record<string, unknown>): ContentPageListItem {
  const source = raw as Record<string, unknown>;

  const name = ensureString(pick(source, "name", "Name"), "Untitled page");
  const slug = ensureSlug(pick(source, "slug", "Slug"), name);

  const updatedSource = pick(source, "updatedAtUtc", "UpdatedAtUtc", "updatedAt", "UpdatedAt");
  const publishedSource = pick(source, "lastPublishedAtUtc", "LastPublishedAtUtc", "publishedAtUtc", "PublishedAtUtc");

  const updatedIso = ensureIsoString(updatedSource, new Date().toISOString());
  const publishedIso =
    publishedSource === null ||
    publishedSource === undefined ||
    (typeof publishedSource === "string" && publishedSource.trim().length === 0)
      ? null
      : ensureIsoString(publishedSource, updatedIso);

  return {
    id: ensureId(pick(source, "id", "Id"), slug, name),
    name,
    slug,
    description: ensureString(pick(source, "description", "Description"), ""),
    updatedAtUtc: updatedIso,
    lastPublishedAtUtc: publishedIso,
    sectionCount: ensureNumber(pick(source, "sectionCount", "SectionCount")),
    textSections: ensureNumber(pick(source, "textSections", "TextSections")),
    imageSections: ensureNumber(pick(source, "imageSections", "ImageSections"))
  };
}

function ensureString(value: unknown, fallback = ""): string {
  if (typeof value === "string") {
    const trimmed = value.trim();
    if (trimmed.length > 0) {
      return trimmed;
    }
  }
  return fallback;
}

function ensureIsoString(value: unknown, fallback: string): IsoDateString {
  if (value instanceof Date) {
    return value.toISOString() as IsoDateString;
  }

  if (typeof value === "number" && Number.isFinite(value)) {
    return new Date(value).toISOString() as IsoDateString;
  }

  if (typeof value === "string") {
    const trimmed = value.trim();
    if (trimmed.length > 0) {
      const parsed = new Date(trimmed);
      if (!Number.isNaN(parsed.getTime())) {
        return trimmed as IsoDateString;
      }
    }
  }

  const parsedFallback = new Date(fallback);
  return (Number.isNaN(parsedFallback.getTime()) ? new Date() : parsedFallback).toISOString() as IsoDateString;
}

function ensureNumber(value: unknown, fallback = 0): number {
  if (typeof value === "number" && Number.isFinite(value)) {
    return value;
  }

  if (typeof value === "string") {
    const trimmed = value.trim();
    if (trimmed.length > 0) {
      const parsed = Number.parseInt(trimmed, 10);
      if (Number.isFinite(parsed)) {
        return parsed;
      }
    }
  }

  return fallback;
}

function pick(source: Record<string, unknown>, ...keys: string[]): unknown {
  for (const key of keys) {
    const candidate = source[key];
    if (candidate !== undefined && candidate !== null) {
      return candidate;
    }
  }
  return undefined;
}

function ensureSlug(candidate: unknown, name: string): string {
  const slugCandidate = ensureString(candidate ?? "", "");
  if (slugCandidate) {
    return slugCandidate;
  }

  const derived = name
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 60);

  return derived || `page-${Math.random().toString(36).slice(2, 10)}`;
}

function ensureId(candidate: unknown, slug: string, name: string): string {
  const idCandidate = ensureString(candidate ?? "", "");
  if (idCandidate) {
    return idCandidate;
  }

  if (slug) {
    return slug;
  }

  const fallback = name
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");

  return fallback || `page-${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`;
}

function hoursSince(date: Date): number {
  return (Date.now() - date.getTime()) / 36e5;
}

function formatFullDate(value: IsoDateString): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : DATE_FULL.format(date);
}

function formatDateTimeShort(value: IsoDateString): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : DATE_TIME_SHORT.format(date);
}

function formatTimeOnly(value: IsoDateString): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : TIME_ONLY.format(date);
}

function createSectionsLoader(): InspectorServices["loadSections"] {
  const cache = new Map<string, InspectorSection[]>();
  return async (pageId) => {
    if (cache.has(pageId)) {
      return cache.get(pageId) ?? [];
    }

    try {
      const result = await fetchSections(pageId, { revalidate: true, ttlMs: 0 });
      const sections = (result.data.sections ?? []).map(mapSectionForInspector);
      cache.set(pageId, sections);
      return sections;
    } catch (error) {
      console.warn("[libraryConsole] Unable to load sections for inspector", error);
      cache.set(pageId, []);
      return [];
    }
  };
}

function createActivityLoader(): InspectorServices["loadActivity"] {
  const cache = new Map<string, ContentHistoryEntry[]>();
  return async (pageId) => {
    if (cache.has(pageId)) {
      return cache.get(pageId) ?? [];
    }

    try {
      await loadDashboardHistory({ pageId, take: 8, force: true, ttlMs: 0 });
      const history = getDashboardHistory(pageId, 8);
      cache.set(pageId, history);
      return history;
    } catch (error) {
      console.warn("[libraryConsole] Unable to load history for inspector", error);
      cache.set(pageId, []);
      return [];
    }
  };
}

function mapSectionForInspector(section: PageSectionWithHistory): InspectorSection {
  return {
    id: section.id,
    sectionKey: section.sectionKey,
    title: section.title,
    contentType: section.contentType,
    updatedAtUtc: section.updatedAtUtc,
    lastPublishedAtUtc: section.lastPublishedAtUtc ?? null
  };
}

function createInspector(root: HTMLElement, services: InspectorServices) {
  const slugEl = root.querySelector<HTMLElement>("[data-library-inspector-slug]");
  const titleEl = root.querySelector<HTMLElement>("[data-library-inspector-title]");
  const descriptionEl = root.querySelector<HTMLElement>("[data-library-inspector-description]");
  const sectionsEl = root.querySelector<HTMLElement>("[data-library-inspector-sections]");
  const textEl = root.querySelector<HTMLElement>("[data-library-inspector-text]");
  const imagesEl = root.querySelector<HTMLElement>("[data-library-inspector-images]");
  const signalsList = root.querySelector<HTMLElement>("[data-library-inspector-signals]");
  const sectionList = root.querySelector<HTMLElement>("[data-library-inspector-section-list]");
  const activityList = root.querySelector<HTMLElement>("[data-library-inspector-activity]");
  const tabs = Array.from(root.querySelectorAll<HTMLButtonElement>("[data-library-inspector-tabs] [data-tab]"));
  const panels = Array.from(root.querySelectorAll<HTMLElement>("[data-library-inspector-panels] [data-panel]"));
  const closers = root.querySelectorAll<HTMLElement>("[data-library-close]");

  let lastTrigger: HTMLElement | null = null;
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

  function open(page: ContentPageListItem, trigger: HTMLElement) {
    lastTrigger = trigger;
    activeTab = "summary";
    updateInspectorTabs(tabs, panels, activeTab);

    renderSummary(page);
    void renderSections(page.id);
    void renderActivity(page.id);

    root.hidden = false;
    requestAnimationFrame(() => {
      root.dataset.state = "open";
      const closeButton = root.querySelector<HTMLButtonElement>(".library-inspector__close");
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

  function renderSummary(page: ContentPageListItem) {
    if (slugEl) slugEl.textContent = page.slug ?? "";
    if (titleEl) titleEl.textContent = page.name ?? "";
    if (descriptionEl) {
      descriptionEl.textContent = page.description ?? "No description captured yet.";
    }
    if (sectionsEl) sectionsEl.textContent = page.sectionCount.toString();
    if (textEl) textEl.textContent = page.textSections.toString();
    if (imagesEl) imagesEl.textContent = page.imageSections.toString();
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

  async function renderSections(pageId: string) {
    if (!sectionList) return;
    sectionList.innerHTML = "";
    const loader = document.createElement("li");
    loader.className = "library-inspector__loading";
    loader.textContent = "Loading sectionsâ€¦";
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
      meta.textContent = `${section.contentType} · Updated ${formatDateTimeShort(section.updatedAtUtc)}`;
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

  async function renderActivity(pageId: string) {
    if (!activityList) return;
    activityList.innerHTML = "";
    const loader = document.createElement("li");
    loader.className = "library-inspector__loading";
    loader.textContent = "Loading activityâ€¦";
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
      meta.textContent = `${entry.performedByDisplayName ?? "Unknown"} · ${formatDateTimeShort(entry.performedAtUtc)}`;
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

function updateInspectorTabs(
  tabs: HTMLButtonElement[],
  panels: HTMLElement[],
  active: string
) {
  tabs.forEach((tab) => {
    const isActive = tab.getAttribute("data-tab") === active;
    tab.dataset.state = isActive ? "active" : "idle";
  });

  panels.forEach((panel) => {
    panel.dataset.state = panel.getAttribute("data-panel") === active ? "active" : "idle";
  });
}

function buildSummarySignals(page: ContentPageListItem): string[] {
  const signals: string[] = [];
  const updated = formatDateTimeShort(page.updatedAtUtc);
  signals.push(`Updated ${updated}`);

  if (page.lastPublishedAtUtc) {
    signals.push(`Last publish ${formatDateTimeShort(page.lastPublishedAtUtc)}`);
    if (new Date(page.updatedAtUtc).getTime() > new Date(page.lastPublishedAtUtc).getTime()) {
      signals.push("Draft changes pending publish");
    }
  } else {
    signals.push("Draft has not been published yet");
  }

  const textRatio = page.sectionCount > 0 ? Math.round((page.textSections / page.sectionCount) * 100) : 0;
  signals.push(`Copy density ${textRatio}% text · ${page.imageSections} imagery slots`);

  return signals;
}

function buildHistorySnippet(entry: ContentHistoryEntry): string {
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

function truncate(value: string, length = 60): string {
  const trimmed = value.replace(/\s+/g, " ").trim();
  return trimmed.length > length ? `${trimmed.slice(0, length)}…` : trimmed;
}
