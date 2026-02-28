(async () => {
    const brandElement = (element) => {
        if (!element || !element.classList) {
            return;
        }
        element.classList.forEach((cls) => {
            if (cls.startsWith("bugence-editor")) {
                const branded = cls.replace("bugence-editor", "bugence-visual-editor");
                element.classList.add(branded);
            }
        });
    };

    const brandTree = (root) => {
        if (!root) return;
        brandElement(root);
        if (root.querySelectorAll) {
            root.querySelectorAll("[class*=\"bugence-editor\"]").forEach((node) => brandElement(node));
        }
    };

    const setBrandClassName = (element, value) => {
        element.className = value;
        brandElement(element);
        return element;
    };

    const extractPageIdFromPath = () => {
        const match = window.location.pathname.match(/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})/i);
        return match ? match[1] : null;
    };
    const fetchConfigFromApi = async (pageId) => {
        try {
            const response = await fetch(`/api/content/pages/${pageId}`, { credentials: "include" });
            if (!response.ok) {
                return null;
            }
            const payload = await response.json();
            const page = payload?.page ?? payload?.Page;
            if (!page) {
                return null;
            }
            const normalizedId = page.id ?? page.Id ?? pageId;
            return {
                pageId: normalizedId,
                pageSlug: page.slug ?? page.Slug,
                pageName: page.name ?? page.Name,
                apiBase: "/api/content",
                selectorHints: {},
                editUrl: normalizedId ? `/Content/Edit/${normalizedId}` : void 0
            };
        } catch (error) {
            console.warn("[Bugence Editor] Unable to hydrate configuration from API.", error);
            return null;
        }
    };
    const resolveConfig = async () => {
        const cached = window.__BugenceVisualEditor || window.__bugenceEditor;
        if (cached && typeof cached === "object") {
            return cached;
        }
        const configScript = document.getElementById("bugence-editor-config");
        if (configScript instanceof HTMLScriptElement) {
            const rawPayload = (configScript.textContent ?? configScript.innerHTML ?? "").trim();
            if (rawPayload.length) {
                try {
                    const parsed = JSON.parse(rawPayload);
                    window.__BugenceVisualEditor = parsed;
                    window.__bugenceEditor = parsed;
                    return parsed;
                } catch (error) {
                    console.error("[Bugence Editor] Unable to parse configuration payload.", error);
                }
            }
        }
        const fallbackId = extractPageIdFromPath();
        if (fallbackId) {
            const hydrated = await fetchConfigFromApi(fallbackId);
            if (hydrated) {
                window.__BugenceVisualEditor = hydrated;
                window.__bugenceEditor = hydrated;
                return hydrated;
            }

            const minimal = { pageId: fallbackId, apiBase: "/api/content", selectorHints: {} };
            window.__BugenceVisualEditor = minimal;
            window.__bugenceEditor = minimal;
            return minimal;
        }
        return null;
    };
    const config = await resolveConfig();
    if (!config || !config.pageId) {
        console.warn("[Bugence Editor] Missing configuration payload.");
        return;
    }

    const API_BASE = config.apiBase ?? "/api/content";
    const PAGE_ENDPOINT = `${API_BASE}/pages/${config.pageId}`;
    const SECTION_ENDPOINT = `${API_BASE}/pages/${config.pageId}/sections`;
    const selectorHints = config.selectorHints ?? {};

    const LOCKED_TAGS = new Set(["NAV", "ASIDE", "HEADER"]);
    const LOCKED_ROLE_HINTS = new Set(["navigation", "menubar"]);
    const LOCKED_ID_HINTS = new Set([
        "gold-shell",
        "gold-nav",
        "gold-links",
        "gold-sub",
        "site-sidebar",
        "site-menu-mobile",
        "canvas-sidebar"
    ]);
    const LOCKED_CLASS_TOKENS = [
        "site-sidebar",
        "canvas-sidebar",
        "global-sidebar",
        "nav-shell",
        "site-nav",
        "navbar",
        "nav",
        "header",
        "sidebar",
        "navbar-nav",
        "topbar",
        "top-nav",
        "side-nav",
        "side-navigation",
        "side-menu",
        "sidenav",
        "sidebar-wrapper",
        "drawer",
        "menu-bar",
        "bugence-editor-sidebar",
        "bugence-visual-editor-sidebar"
    ];
    const LOCK_DATA_FLAGS = new Set(["true", "1", "locked"]);

    const HARD_LOCK_SELECTORS = [
        ".site-sidebar",
        ".canvas-sidebar",
        ".global-sidebar",
        ".navbar",
        ".navbar-nav",
        ".nav",
        "nav",
        "header",
        ".header",
        ".topbar",
        ".top-nav",
        ".side-nav",
        ".side-navigation",
        ".side-menu",
        ".sidenav",
        ".sidebar",
        ".sidebar-wrapper",
        ".drawer",
        ".menu-bar"
    ];
    const isElementHardLocked = (element) => {
        if (!element || !(element instanceof Element))
            return false;
        return HARD_LOCK_SELECTORS.some((sel) => {
            try {
                return element.matches(sel) || element.closest(sel);
            }
            catch (_) {
                return false;
            }
        });
    };

    const shouldLockElement = (element) => {
        if (!element) {
            return false;
        }
        let current = element;
        while (current && current !== document.body) {
            if (!(current instanceof HTMLElement)) {
                current = current.parentElement;
                continue;
            }
            if (current.dataset?.bugenceIgnore === "true") {
                return false;
            }
            const lockFlag = current.dataset?.bugenceLock ?? current.dataset?.bugenceLocked;
            if (lockFlag && LOCK_DATA_FLAGS.has(lockFlag.toLowerCase())) {
                return true;
            }
            if (current.dataset?.bugenceLockRegion) {
                return true;
            }
            if (isElementHardLocked(current)) {
                return true;
            }
            if (LOCKED_ID_HINTS.has(current.id)) {
                return true;
            }
            if (LOCKED_TAGS.has(current.tagName)) {
                return true;
            }
            const role = (current.getAttribute("role") ?? "").toLowerCase();
            if (role && LOCKED_ROLE_HINTS.has(role)) {
                return true;
            }
            if (LOCKED_CLASS_TOKENS.some((token) => current.classList.contains(token))) {
                return true;
            }
            current = current.parentElement;
        }
        return false;
    };
    const BLOCKED_TAGS = new Set([
        "HTML",
        "BODY",
        "HEAD",
        "SCRIPT",
        "STYLE",
        "LINK",
        "META",
        "TITLE",
        "NOSCRIPT",
        "SVG",
        "PATH",
        "CIRCLE",
        "RECT",
        "VIDEO",
        "AUDIO",
        "SOURCE",
        "CANVAS"
    ]);

    const fetchWithTimeout = async (input, init = {}) => {
        const timeout = init.timeoutMs ?? 12000;
        const controller = new AbortController();
        const id = window.setTimeout(() => controller.abort(), timeout);
        try {
            return await fetch(input, { ...init, signal: controller.signal });
        } finally {
            window.clearTimeout(id);
        }
    };

    const state = {
        sectionsById: new Map(),
        sectionsByKey: new Map(),
        sectionsBySelector: new Map(),
        elementSections: new WeakMap(),
        badge: null,
        badgeLockedTag: null,
        activeElement: null,
        overlay: null,
        toastStack: null,
        sidebar: null,
        page: null,
        lastPublishedAt: null,
        lastUpdatedAt: null,
        hasDraftChanges: false,
        isSaving: false,
        isPublishing: false,
        selectionHandler: null
    };

    const isImageElement = (element) => element && element.tagName === "IMG";

    const isLocked = (section) => section && section.isLocked;

    const ensureToastStack = () => {
        if (state.toastStack) {
            return state.toastStack;
        }

        const stack = document.createElement("div");
        stack.className = "bugence-editor-toast-stack";
        document.body.appendChild(stack);
        brandTree(stack);
        state.toastStack = stack;
        return stack;
    };

    const showToast = (message, tone = "info") => {
        const stack = ensureToastStack();
        const toast = document.createElement("div");
        toast.className = "bugence-editor-toast";
        if (tone === "error") {
            toast.classList.add("bugence-editor-toast--error");
        } else if (tone === "warning") {
            toast.classList.add("bugence-editor-toast--warning");
        }
        toast.textContent = message;
        brandElement(toast);
        stack.appendChild(toast);

        window.setTimeout(() => {
            toast.classList.add("bugence-editor-toast--leaving");
            toast.style.opacity = "0";
            toast.style.transform = "translateY(6px)";
            window.setTimeout(() => {
                toast.remove();
            }, 260);
        }, 2600);
    };

    const normalizeSection = (section) => {
        if (!section) {
            return section;
        }

        if (section.Id && !section.id) {
            section.id = section.Id;
        } else if (section.id && !section.Id) {
            section.Id = section.id;
        }

        if (section.SectionKey && !section.sectionKey) {
            section.sectionKey = section.SectionKey;
        } else if (section.sectionKey && !section.SectionKey) {
            section.SectionKey = section.sectionKey;
        }

        if (section.ContentType && !section.contentType) {
            section.contentType = section.ContentType;
        } else if (section.contentType && !section.ContentType) {
            section.ContentType = section.contentType;
        }

        const updated = section.updatedAtUtc ?? section.UpdatedAtUtc;
        if (updated) {
            section.updatedAtUtc = updated;
            section.UpdatedAtUtc = updated;
        }

        const published = section.lastPublishedAtUtc ?? section.LastPublishedAtUtc;
        if (published) {
            section.lastPublishedAtUtc = published;
            section.LastPublishedAtUtc = published;
        }

        return section;
    };

    const parseIsoDate = (value) => {
        if (!value) {
            return null;
        }

        if (value instanceof Date) {
            return new Date(value.getTime());
        }

        const parsed = new Date(value);
        return Number.isNaN(parsed.getTime()) ? null : parsed;
    };

    const formatTimestamp = (date) => {
        if (!date) {
            return "";
        }

        try {
            return date.toLocaleString(undefined, {
                month: "short",
                day: "2-digit",
                hour: "2-digit",
                minute: "2-digit"
            });
        } catch (error) {
            console.warn("[Bugence Editor] Unable to format timestamp", error);
            return date.toISOString();
        }
    };

    const recomputeSectionMetrics = () => {
        let lastPublished = null;
        let lastUpdated = null;
        let hasDrafts = false;

        state.sectionsById.forEach((section) => {
            normalizeSection(section);

            const updatedAt = parseIsoDate(section.updatedAtUtc ?? section.UpdatedAtUtc);
            if (updatedAt && (!lastUpdated || updatedAt > lastUpdated)) {
                lastUpdated = updatedAt;
            }

            const publishedAt = parseIsoDate(section.lastPublishedAtUtc ?? section.LastPublishedAtUtc);
            if (publishedAt && (!lastPublished || publishedAt > lastPublished)) {
                lastPublished = publishedAt;
            }

            if (updatedAt && (!publishedAt || updatedAt > publishedAt)) {
                hasDrafts = true;
            }
        });

        state.lastPublishedAt = lastPublished;
        state.lastUpdatedAt = lastUpdated;
        state.hasDraftChanges = hasDrafts;
    };

    const applyPublishTimestamp = (value) => {
        const timestamp = parseIsoDate(value) ?? new Date();
        const iso = timestamp.toISOString();

        state.sectionsById.forEach((section) => {
            normalizeSection(section);
            section.lastPublishedAtUtc = iso;
            section.LastPublishedAtUtc = iso;
        });

        state.lastPublishedAt = timestamp;
        state.hasDraftChanges = false;
    };

    const ensureSidebar = () => {
        if (state.sidebar) {
            return state.sidebar;
        }

        const sidebar = document.createElement("aside");
        sidebar.className = "bugence-editor-sidebar";
        sidebar.dataset.bugenceIgnore = "true";

        const header = document.createElement("header");
        header.className = "bugence-editor-sidebar__header";

        const badge = document.createElement("span");
        badge.className = "bugence-editor-sidebar__badge";
        badge.textContent = "Visual Editor";

        const title = document.createElement("h2");
        title.className = "bugence-editor-sidebar__title";

        const slug = document.createElement("p");
        slug.className = "bugence-editor-sidebar__slug";

        header.append(badge, title, slug);

        const body = document.createElement("div");
        body.className = "bugence-editor-sidebar__body";

        const status = document.createElement("div");
        status.className = "bugence-editor-sidebar__status";

        const statusDot = document.createElement("span");
        statusDot.className = "bugence-editor-sidebar__dot";

        const statusText = document.createElement("div");

        const statusLabel = document.createElement("p");
        statusLabel.className = "bugence-editor-sidebar__status-label";

        const statusMeta = document.createElement("p");
        statusMeta.className = "bugence-editor-sidebar__status-meta";

        statusText.append(statusLabel, statusMeta);
        status.append(statusDot, statusText);
        body.append(status);

        const stats = document.createElement("div");
        stats.className = "bugence-editor-sidebar__stats";

        const createStat = (label, icon) => {
            const wrapper = document.createElement("div");
            wrapper.className = "bugence-editor-sidebar__stat";
            if (icon) {
                wrapper.dataset.icon = icon;
            }

            const value = document.createElement("span");
            value.className = "bugence-editor-sidebar__stat-value";
            value.textContent = "\u2014";

            const statLabel = document.createElement("span");
            statLabel.className = "bugence-editor-sidebar__stat-label";
            statLabel.textContent = label;

            wrapper.append(value, statLabel);
            stats.append(wrapper);
            return value;
        };

        const sectionsValue = createStat("Editable sections", "\u25A3");
        const draftValue = createStat("Pending edits", "\u2732");
        const publishValue = createStat("Last publish", "\u23F1");

        body.append(stats);

        const hint = document.createElement("p");
        hint.className = "bugence-editor-sidebar__hint";
        hint.textContent = "Double-click any highlighted block to edit. Save changes, then publish to sync the live page.";
        body.append(hint);

        const footer = document.createElement("div");
        footer.className = "bugence-editor-sidebar__footer";

        const publishButton = document.createElement("button");
        publishButton.type = "button";
        publishButton.className = "bugence-editor-sidebar__publish";
        publishButton.dataset.bugenceIgnore = "true";

        const publishIcon = document.createElement("span");
        publishIcon.className = "bugence-editor-sidebar__publish-icon";
        publishIcon.innerHTML = `<svg viewBox="0 0 16 16" width="16" height="16" fill="none" stroke="currentColor" stroke-width="1.5"><path d="M8 1v9"></path><path d="M4 5l4-4 4 4"></path><path d="M3 10h10v5H3z"></path></svg>`;

        const publishLabel = document.createElement("span");
        publishLabel.className = "bugence-editor-sidebar__publish-label";
        publishLabel.textContent = "Publish live";

        publishButton.append(publishIcon, publishLabel);
        publishButton.addEventListener("click", handlePublishClick);

        footer.append(publishButton);

        let dashboardLink = null;
        if (typeof config.editUrl === "string" && config.editUrl.length) {
            dashboardLink = document.createElement("a");
            dashboardLink.className = "bugence-editor-sidebar__link";
            dashboardLink.href = config.editUrl;
            dashboardLink.target = "_blank";
            dashboardLink.rel = "noopener";
            dashboardLink.textContent = "Open dashboard";
            footer.append(dashboardLink);
        }

        let liveLink = null;
        if (typeof config.pageAsset === "string" && config.pageAsset.length) {
            liveLink = document.createElement("a");
            liveLink.className = "bugence-editor-sidebar__link";
            liveLink.href = config.pageAsset;
            liveLink.target = "_blank";
            liveLink.rel = "noopener";
            liveLink.textContent = "View live page";
            footer.append(liveLink);
        }

        sidebar.append(header, body, footer);
        document.body.appendChild(sidebar);
        brandTree(sidebar);

        state.sidebar = {
            root: sidebar,
            title,
            slug,
            statusLabel,
            statusMeta,
            statusDot,
            sectionsValue,
            draftValue,
            publishValue,
            publishButton,
            publishLabel,
            liveLink,
            dashboardLink
        };

        updateSidebarPageInfo();
        return state.sidebar;
    };

    const updateSidebarPageInfo = () => {
        if (!state.sidebar) {
            return;
        }

        const pageName = state.page?.name ?? config.pageName ?? "Page";
        const pageSlug = state.page?.slug ?? config.pageSlug ?? "";

        state.sidebar.title.textContent = pageName;
        state.sidebar.slug.textContent = pageSlug || "unpublished";

        if (state.sidebar.liveLink && typeof config.pageAsset === "string" && config.pageAsset.length) {
            state.sidebar.liveLink.href = config.pageAsset;
        }
    };

    const refreshSidebar = () => {
        const sidebarRefs = ensureSidebar();
        updateSidebarPageInfo();
        recomputeSectionMetrics();

        const sidebarRoot = sidebarRefs.root;
        const publishButton = sidebarRefs.publishButton;
        const publishLabel = sidebarRefs.publishLabel;
        const statusLabel = sidebarRefs.statusLabel;
        const statusMeta = sidebarRefs.statusMeta;
        const hasSections = state.sectionsById.size > 0;

        if (sidebarRefs.statusDot) {
            sidebarRefs.statusDot.dataset.state = hasSections ? (state.hasDraftChanges ? "draft" : "live") : "empty";
        }

        if (sidebarRefs.sectionsValue) {
            sidebarRefs.sectionsValue.textContent = hasSections ? String(state.sectionsById.size) : "0";
        }

        if (!hasSections) {
            sidebarRoot.dataset.bugenceDraft = "false";
            statusLabel.textContent = "No editable sections";
            statusMeta.textContent = "This page has no editable regions yet.";
            publishButton.disabled = true;
            delete publishButton.dataset.state;
            publishLabel.textContent = "Publish live";
            if (sidebarRefs.draftValue) {
                sidebarRefs.draftValue.textContent = "No";
            }
            if (sidebarRefs.publishValue) {
                sidebarRefs.publishValue.textContent = "\u2014";
            }
            return;
        }

        sidebarRoot.dataset.bugenceDraft = state.hasDraftChanges ? "true" : "false";
        if (state.hasDraftChanges) {
            statusLabel.textContent = "Draft changes ready";
            statusMeta.textContent = state.lastUpdatedAt ? `Last edit ${formatTimestamp(state.lastUpdatedAt)}` : "Recent edits pending publish.";
            if (sidebarRefs.draftValue) {
                sidebarRefs.draftValue.textContent = "Yes";
            }
        } else {
            statusLabel.textContent = "Live";
            statusMeta.textContent = state.lastPublishedAt ? `Published ${formatTimestamp(state.lastPublishedAt)}` : "Not yet published";
            if (sidebarRefs.draftValue) {
                sidebarRefs.draftValue.textContent = "No";
            }
        }

        if (sidebarRefs.publishValue) {
            sidebarRefs.publishValue.textContent = state.lastPublishedAt
                ? formatTimestamp(state.lastPublishedAt)
                : "\u2014";
        }

        publishButton.disabled = state.isPublishing;
        if (state.isPublishing) {
            publishLabel.textContent = "Publishing...";
            publishButton.dataset.state = "loading";
        } else {
            publishLabel.textContent = "Publish live";
            delete publishButton.dataset.state;
        }
    };

    async function handlePublishClick(event) {
        event.preventDefault();
        if (state.isPublishing) {
            return;
        }

        state.isPublishing = true;
        refreshSidebar();

        try {
            const response = await fetch(`${API_BASE}/pages/${config.pageId}/publish`, {
                method: "POST",
                credentials: "include"
            });

            let payload = null;
            try {
                payload = await response.json();
            } catch (error) {
                payload = null;
            }

            if (!response.ok) {
                const message = payload?.message ?? payload?.Message ?? "Unable to publish page.";
                throw new Error(message);
            }

            const publishedAtValue = payload?.publishedAtUtc ?? payload?.PublishedAtUtc ?? new Date().toISOString();
            applyPublishTimestamp(publishedAtValue);

            const successMessage = payload?.message ?? payload?.Message ?? "Page published to live experience.";
            showToast(successMessage);
            if (Array.isArray(payload?.warnings)) {
                payload.warnings
                    .filter((warning) => typeof warning === "string" && warning.trim().length > 0)
                    .forEach((warning) => showToast(`Skipped: ${warning}`, "warning"));
            }
        } catch (error) {
            console.error(error);
            showToast(error?.message ?? "Unable to publish page.", "error");
        } finally {
            state.isPublishing = false;
            refreshSidebar();
        }
    }

    const ensureBadge = () => {
        if (state.badge) {
            return state.badge;
        }

        const badge = document.createElement("button");
        badge.type = "button";
        badge.className = "bugence-editor-badge";
        badge.innerHTML = `
            <span class="bugence-editor-badge__icon" aria-hidden="true">
                <svg viewBox="0 0 14 14" width="14" height="14" fill="none" stroke="currentColor" stroke-width="1.6">
                    <path d="M9.5 1.5l3 3-7.5 7.5-3.5 1 1-3.5z"></path>
                    <path d="M8.5 2.5l3 3"></path>
                </svg>
            </span>
            <span class="bugence-editor-badge__label">Edit</span>
        `;

        badge.addEventListener("click", (event) => {
            event.preventDefault();
            event.stopPropagation();
            if (state.activeElement) {
                openEditor(state.activeElement);
            }
        });

        const lockedTag = document.createElement("span");
        lockedTag.className = "bugence-editor-badge__locked";
        lockedTag.textContent = "Locked";
        lockedTag.hidden = true;
        badge.appendChild(lockedTag);

        state.badgeLockedTag = lockedTag;
        document.body.appendChild(badge);
        brandTree(badge);
        state.badge = badge;
        return badge;
    };

    const hideBadge = () => {
        if (!state.badge) {
            return;
        }

        state.badge.style.display = "none";
        state.activeElement?.classList?.remove("bugence-editor-highlight");
        state.activeElement?.classList?.remove("bugence-editor-highlight--locked");
        state.activeElement = null;
    };

    const positionBadge = (element, section) => {
        const badge = ensureBadge();
        const rect = element.getBoundingClientRect();
        const top = window.scrollY + rect.top + 4;
        const left = window.scrollX + rect.right - 4;

        badge.style.top = `${top}px`;
        badge.style.left = `${left}px`;
        const locked = isLocked(section) || isElementHardLocked(element);
        badge.dataset.state = locked ? "locked" : "ready";
        badge.classList.add("bugence-editor-badge--visible");
        badge.style.display = "inline-flex";

        if (state.badgeLockedTag) {
            state.badgeLockedTag.hidden = !(isLocked(section) || isElementHardLocked(element));
        }
    };

    const sanitizeTarget = (element) => {
        if (!element) {
            return null;
        }

        let current = element;
        const baseCandidate = current?.nodeType === Node.ELEMENT_NODE ? current : current?.parentElement ?? null;

        while (current && current !== document.body) {
            if (state.overlay && state.overlay.contains(current)) {
                return null;
            }

            if (current.nodeType !== Node.ELEMENT_NODE) {
                current = current.parentElement ?? current.ownerDocument?.body ?? null;
                continue;
            }

            if (BLOCKED_TAGS.has(current.tagName)) {
                return null;
            }

            if (current.dataset && current.dataset.bugenceIgnore === "true") {
                return null;
            }

            if (shouldLockElement(current)) {
                return null;
            }

            if (current.childElementCount === 0 || isImageElement(current)) {
                return current;
            }

            const meaningfulText = Array.from(current.childNodes)
                .filter((node) => node.nodeType === Node.TEXT_NODE)
                .map((node) => node.textContent?.trim() ?? "")
                .join("");

            if (meaningfulText.length >= 4) {
                return current;
            }

            current = current.firstElementChild ?? current.nextElementSibling ?? current.parentElement;
        }

        return baseCandidate instanceof Element ? baseCandidate : null;
    };

    const computeSelector = (element) => {
        if (!element) {
            return null;
        }

        if (element.id) {
            return `#${CSS.escape(element.id)}`;
        }

        const segments = [];
        let current = element;

        while (current && current !== document.body) {
            let segment = current.tagName.toLowerCase();

            const classes = current.classList ? Array.from(current.classList).filter(Boolean) : [];
            if (classes.length > 0) {
                segment += `.${classes.map((cls) => CSS.escape(cls)).join(".")}`;
            }

            const parent = current.parentElement;
            if (parent) {
                const siblings = Array.from(parent.children).filter((child) => child.tagName === current.tagName);
                if (siblings.length > 1) {
                    const index = siblings.indexOf(current) + 1;
                    segment += `:nth-of-type(${index})`;
                }
            }

            segments.unshift(segment);
            current = current.parentElement;
        }

        return segments.length ? segments.join(" > ") : null;
    };

    const rememberSectionMapping = (element, section) => {
        if (!element || !section) {
            return;
        }

        normalizeSection(section);

        if (section.sectionKey) {
            element.dataset.bugenceSection = section.sectionKey;
            state.sectionsByKey.set(section.sectionKey, section);
        }

        if (section.cssSelector) {
            state.sectionsBySelector.set(section.cssSelector, section);
        }

        const sectionId = section.id ?? section.Id;
        if (sectionId) {
            section.id = sectionId;
            section.Id = sectionId;
            state.sectionsById.set(sectionId, section);
        }
        state.elementSections.set(element, section);
    };

    const findElementForSection = (section) => {
        if (!section) {
            return null;
        }

        if (section.sectionKey) {
            const existing = document.querySelector(`[data-bugence-section="${section.sectionKey}"]`);
            if (existing) {
                return existing;
            }
        }

        if (section.cssSelector) {
            try {
                const match = document.querySelector(section.cssSelector);
                if (match) {
                    return match;
                }
            } catch (error) {
                console.warn("[Bugence Editor] Invalid selector for section", section.sectionKey, error);
            }
        }

        return null;
    };

    const hydrateSectionElement = (section) => {
        if (section) {
            const legacyPrevious = section.previousContentValue ?? section.PreviousContentValue;
            if (typeof legacyPrevious === "string") {
                section.previousContentValue = legacyPrevious;
            }
            normalizeSection(section);
        }

        const element = findElementForSection(section);
        if (!element) {
            return;
        }

        if (section.sectionKey) {
            element.dataset.bugenceSection = section.sectionKey;
        }

        if (isImageElement(element)) {
            if (section.mediaPath) {
                element.src = section.mediaPath;
            }
            if (section.mediaAltText) {
                element.alt = section.mediaAltText;
            }
        } else if (typeof section.contentValue === "string" && section.contentValue.length) {
            element.innerHTML = section.contentValue;
        }

        rememberSectionMapping(element, section);
    };

    const resolveHintSelector = (sectionKey) => {
        const raw = selectorHints?.[sectionKey];
        if (!raw) {
            return null;
        }

        const candidates = raw.split(",").map((part) => part.trim()).filter(Boolean);
        for (const candidate of candidates) {
            try {
                const match = document.querySelector(candidate);
                if (match) {
                    return candidate;
                }
            } catch (error) {
                console.warn("[Bugence Editor] Invalid selector hint", candidate, error);
            }
        }

        return null;
    };

    const registerSections = (sections) => {
        sections.forEach((section) => {
            if (section) {
                const legacyPrevious = section.previousContentValue ?? section.PreviousContentValue;
                normalizeSection(section);
                if (typeof legacyPrevious === "string") {
                    section.previousContentValue = legacyPrevious;
                }
            }

            const selectorLooksValid = (() => {
                if (!section.cssSelector) {
                    return false;
                }

                try {
                    return Boolean(document.querySelector(section.cssSelector));
                } catch (error) {
                    console.warn("[Bugence Editor] Stored selector is invalid", section.cssSelector, error);
                    return false;
                }
            })();

            if (!selectorLooksValid) {
                const hintSelector = resolveHintSelector(section.sectionKey);
                if (hintSelector) {
                    section.cssSelector = hintSelector;
                }
            }

            hydrateSectionElement(section);
        });
        refreshSidebar();
    };

    const getSectionForElement = (element) => {
        if (!element) {
            return null;
        }

        if (state.elementSections.has(element)) {
            return state.elementSections.get(element);
        }

        const key = element.dataset?.bugenceSection;
        if (key && state.sectionsByKey.has(key)) {
            const section = state.sectionsByKey.get(key);
            state.elementSections.set(element, section);
            return section;
        }

        const selector = computeSelector(element);
        if (selector && state.sectionsBySelector.has(selector)) {
            const section = state.sectionsBySelector.get(selector);
            state.elementSections.set(element, section);
            return section;
        }

        for (const key of Object.keys(selectorHints)) {
            const hintSelector = resolveHintSelector(key);
            if (!hintSelector) {
                continue;
            }

            try {
                if (element.matches(hintSelector)) {
                    const hintedSection = state.sectionsByKey.get(key) ?? state.sectionsBySelector.get(hintSelector);
                    if (hintedSection) {
                        state.elementSections.set(element, hintedSection);
                        return hintedSection;
                    }
                }
            } catch (error) {
                console.warn("[Bugence Editor] Failed to evaluate selector hint", hintSelector, error);
            }
        }

        return null;
    };

    const handleHover = (event) => {
        const candidate = sanitizeTarget(event.target);
        if (!candidate || candidate === state.activeElement) {
            return;
        }

        if (shouldLockElement(candidate)) {
            hideBadge();
            return;
        }

        if (state.activeElement && state.activeElement !== candidate) {
            state.activeElement.classList?.remove("bugence-editor-highlight");
            state.activeElement.classList?.remove("bugence-editor-highlight--locked");
        }

        const section = getSectionForElement(candidate);

        if (section && isLocked(section)) {
            candidate.classList?.add("bugence-editor-highlight--locked");
        } else {
            candidate.classList?.remove("bugence-editor-highlight--locked");
        }

        candidate.classList?.add("bugence-editor-highlight");
        state.activeElement = candidate;
        positionBadge(candidate, section);
    };

    const handleMouseOut = (event) => {
        if (!state.activeElement) {
            return;
        }

        const related = event.relatedTarget;
        if (related && (related === state.activeElement || related === state.badge || state.badge?.contains(related))) {
            return;
        }

        hideBadge();
    };

    const ensureOverlay = () => {
        if (state.overlay) {
            return state.overlay;
        }

        const overlay = document.createElement("div");
        overlay.className = "bugence-editor-overlay";
        overlay.dataset.bugenceIgnore = "true";
        overlay.addEventListener("click", (event) => {
            if (event.target === overlay) {
                closeOverlay();
            }
        });

        brandTree(overlay);
        state.overlay = overlay;
        return overlay;
    };

    const closeOverlay = () => {
        if (state.overlay) {
            state.overlay.remove();
            state.overlay = null;
        }

        if (typeof state.selectionHandler === "function") {
            document.removeEventListener("selectionchange", state.selectionHandler);
            state.selectionHandler = null;
        }
    };

    const execCommand = (surface, command, value) => {
        surface.focus({ preventScroll: true });

        if (command === "createLink") {
            const url = window.prompt("Enter URL");
            if (!url) {
                document.execCommand("unlink");
                return;
            }

            const normalized = /^https?:\/\//i.test(url) ? url : `https://${url}`;
            document.execCommand("createLink", false, normalized);
            return;
        }

        if (command === "formatBlock") {
            document.execCommand("formatBlock", false, value ?? "p");
            return;
        }

        if (command === "removeFormat") {
            document.execCommand("removeFormat");
            document.execCommand("unlink");
            return;
        }

        if (command === "unlink" || command === "undo" || command === "redo" || command === "insertHorizontalRule") {
            document.execCommand(command);
            return;
        }

        if (command === "foreColor" || command === "hiliteColor") {
            const colorValue = value ?? (command === "hiliteColor" ? "transparent" : "inherit");
            try {
                document.execCommand("styleWithCSS", false, true);
            } catch (error) {
                // ignore
            }

            document.execCommand(command, false, colorValue);

            try {
                document.execCommand("styleWithCSS", false, false);
            } catch (error) {
                // ignore
            }
            return;
        }

        document.execCommand(command, false, value ?? null);
    };

    const buildToolbar = (surface) => {
        const toolbar = document.createElement("div");
        toolbar.className = "bugence-editor-toolbar";

        const trackedButtons = [];
        const statefulCommands = new Set([
            "bold",
            "italic",
            "underline",
            "strikeThrough",
            "superscript",
            "subscript",
            "insertOrderedList",
            "insertUnorderedList",
            "justifyLeft",
            "justifyCenter",
            "justifyRight",
            "justifyFull"
        ]);

        const registerButton = (button, config) => {
            button.dataset.command = config.command;
            if (config.value) {
                button.dataset.value = config.value;
            }
            if (config.toggles) {
                button.dataset.toggles = "true";
            }
            trackedButtons.push({ element: button, config });
        };

        const createButton = (config) => {
            const button = document.createElement("button");
            button.type = "button";
            button.textContent = config.label;
            if (config.title) {
                button.title = config.title;
            }

            button.addEventListener("click", (event) => {
                event.preventDefault();
                execCommand(surface, config.command, config.value);
                updateStates();
            });

            registerButton(button, config);
            return button;
        };

        const createSwatch = (group, config) => {
            const swatch = document.createElement("button");
            swatch.type = "button";
            swatch.className = "bugence-editor-toolbar__swatch";
            swatch.dataset.command = group.command;
            swatch.dataset.value = config.value;
            swatch.title = config.label;
            swatch.setAttribute("aria-label", config.label);
            swatch.style.background = config.preview ?? config.value;

            if (config.value === "transparent") {
                swatch.style.background = "linear-gradient(135deg, rgba(248, 250, 252, 0.9), rgba(148, 163, 184, 0.2))";
            }

            swatch.addEventListener("click", (event) => {
                event.preventDefault();
                execCommand(surface, group.command, config.value);
                Array.from(swatch.parentElement.querySelectorAll(".bugence-editor-toolbar__swatch")).forEach((node) => {
                    node.dataset.selected = node === swatch ? "true" : "false";
                });
                updateStates();
            });

            return swatch;
        };

        const groups = [
            {
                label: "History",
                buttons: [
                    { label: "Undo", command: "undo", title: "Undo (Ctrl/Cmd + Z)" },
                    { label: "Redo", command: "redo", title: "Redo (Ctrl/Cmd + Y)" }
                ]
            },
            {
                label: "Type",
                buttons: [
                    { label: "Bold", command: "bold", toggles: true },
                    { label: "Italic", command: "italic", toggles: true },
                    { label: "Underline", command: "underline", toggles: true },
                    { label: "Strike", command: "strikeThrough", toggles: true },
                    { label: "Superscript", command: "superscript", toggles: true },
                    { label: "Subscript", command: "subscript", toggles: true }
                ]
            },
            {
                label: "Structure",
                buttons: [
                    { label: "Heading", command: "formatBlock", value: "h2", title: "Apply H2" },
                    { label: "Subheading", command: "formatBlock", value: "h3", title: "Apply H3" },
                    { label: "Paragraph", command: "formatBlock", value: "p", title: "Reset to paragraph" },
                    { label: "Quote", command: "formatBlock", value: "blockquote", title: "Block quote" },
                    { label: "Rule", command: "insertHorizontalRule", title: "Insert horizontal divider" }
                ]
            },
            {
                label: "Lists",
                buttons: [
                    { label: "Bullet", command: "insertUnorderedList", toggles: true },
                    { label: "Numbered", command: "insertOrderedList", toggles: true }
                ]
            },
            {
                label: "Alignment",
                buttons: [
                    { label: "Left", command: "justifyLeft" },
                    { label: "Center", command: "justifyCenter" },
                    { label: "Right", command: "justifyRight" },
                    { label: "Justify", command: "justifyFull" }
                ]
            },
            {
                label: "Links",
                buttons: [
                    { label: "Link", command: "createLink" },
                    { label: "Unlink", command: "unlink" },
                    { label: "Clear", command: "removeFormat" }
                ]
            }
        ];

        const palettes = [
            {
                label: "Text",
                command: "foreColor",
                swatches: [
                    { label: "Night", value: "#0f172a" },
                    { label: "Sky", value: "#38bdf8" },
                    { label: "Emerald", value: "#34d399" },
                    { label: "Sunset", value: "#f97316" },
                    { label: "Coral", value: "#fb7185" },
                    { label: "Soft White", value: "#f8fafc" }
                ]
            },
            {
                label: "Highlight",
                command: "hiliteColor",
                swatches: [
                    { label: "None", value: "transparent", preview: "linear-gradient(135deg, rgba(148,163,184,0.35), rgba(15,23,42,0.25))" },
                    { label: "Lemon", value: "#fef08a" },
                    { label: "Ice", value: "#bfdbfe" },
                    { label: "Mint", value: "#bbf7d0" },
                    { label: "Rose", value: "#fbcfe8" },
                    { label: "Glow", value: "#fde68a" }
                ]
            }
        ];

        groups.forEach((group, groupIndex) => {
            const container = document.createElement("div");
            container.className = "bugence-editor-toolbar__group";

            if (group.label) {
                const label = document.createElement("span");
                label.className = "bugence-editor-toolbar__label";
                label.textContent = group.label;
                container.appendChild(label);
            }

            group.buttons.forEach((buttonConfig) => {
                container.appendChild(createButton(buttonConfig));
            });

            toolbar.appendChild(container);

            if (groupIndex < groups.length - 1 || palettes.length > 0) {
                const divider = document.createElement("span");
                divider.className = "bugence-editor-toolbar__divider";
                toolbar.appendChild(divider);
            }
        });

        palettes.forEach((palette, paletteIndex) => {
            const container = document.createElement("div");
            container.className = "bugence-editor-toolbar__group";

            if (palette.label) {
                const label = document.createElement("span");
                label.className = "bugence-editor-toolbar__label";
                label.textContent = palette.label;
                container.appendChild(label);
            }

            const paletteWrapper = document.createElement("div");
            paletteWrapper.className = "bugence-editor-toolbar__palette";

            palette.swatches.forEach((swatchConfig) => {
                paletteWrapper.appendChild(createSwatch(palette, swatchConfig));
            });

            container.appendChild(paletteWrapper);
            toolbar.appendChild(container);

            if (paletteIndex < palettes.length - 1) {
                const divider = document.createElement("span");
                divider.className = "bugence-editor-toolbar__divider";
                toolbar.appendChild(divider);
            }
        });

        const updateStates = () => {
            let blockContext = null;
            try {
                blockContext = document.queryCommandValue("formatBlock");
            } catch (error) {
                blockContext = null;
            }

            trackedButtons.forEach(({ element, config }) => {
                let isActive = false;

                if (config.command === "formatBlock" && config.value) {
                    const normalizedBlock = blockContext ? blockContext.toLowerCase() : "";
                    const expected = config.value.toLowerCase();
                    isActive = normalizedBlock === expected || normalizedBlock === `<${expected}>`;
                } else if (statefulCommands.has(config.command) || config.toggles) {
                    try {
                        isActive = document.queryCommandState(config.command);
                    } catch (error) {
                        isActive = false;
                    }
                }

                element.dataset.active = isActive ? "true" : "false";
            });
        };

        const handleSelectionChange = () => {
            const selection = document.getSelection();
            if (!selection) {
                return;
            }

            const anchorNode = selection.anchorNode;
            if (!anchorNode) {
                return;
            }

            if (surface.contains(anchorNode)) {
                updateStates();
            }
        };

        toolbar.updateStates = updateStates;
        toolbar.handleSelectionChange = handleSelectionChange;

        return toolbar;
    };

    const saveSection = async (element, payload) => {
        if (state.isSaving) {
            return null;
        }

        state.isSaving = true;
        try {
            const response = await fetch(SECTION_ENDPOINT, {
                method: "POST",
                body: payload,
                credentials: "include"
            });

            if (!response.ok) {
                const errorJson = await response.json().catch(() => ({}));
                throw new Error(errorJson.message || "Unable to save content.");
            }

            const json = await response.json();
            if (json.section) {
                hydrateSectionElement(json.section);
                refreshSidebar();
            }

            return json;
        } finally {
            state.isSaving = false;
        }
    };

    const openTextEditor = (element, section) => {
        const overlay = ensureOverlay();
        overlay.innerHTML = "";

        const modal = document.createElement("article");
        modal.className = "bugence-editor-modal";
        modal.dataset.bugenceIgnore = "true";

        const anchorTarget = element?.closest?.("a") || (element?.tagName === "A" ? element : null);
        const baselineHref = anchorTarget ? anchorTarget.getAttribute("href") || "" : "";
        const baselineTargetBlank = anchorTarget ? anchorTarget.getAttribute("target") === "_blank" : false;

        const header = document.createElement("header");
        header.className = "bugence-editor-modal__header";
        header.innerHTML = `
            <div>
                <div class="bugence-editor-modal__title">${section?.title ?? "Edit content"}</div>
                <p class="bugence-editor-modal__subtitle">${config.pageName ?? "Page"} · Rich text</p>
            </div>
        `;

        const closeBtn = document.createElement("button");
        closeBtn.type = "button";
        closeBtn.className = "bugence-editor-modal__close";
        closeBtn.setAttribute("aria-label", "Close editor");
        closeBtn.innerHTML = "&times;";
        closeBtn.addEventListener("click", () => closeOverlay());
        header.appendChild(closeBtn);

        const body = document.createElement("div");
        body.className = "bugence-editor-modal__body";

        const normalizeHtml = (value) => (value ?? "").replace(/&nbsp;/g, " ").replace(/\s+/g, " ").trim();

        const baselineContent = section?.contentValue ?? element.innerHTML;
        let currentBaseline = baselineContent;
        const previousRevision = section?.previousContentValue ?? null;
        const hasPreviousRevision = typeof previousRevision === "string" && normalizeHtml(previousRevision) !== normalizeHtml(currentBaseline);

        const surface = document.createElement("div");
        surface.className = "bugence-editor-surface";
        surface.contentEditable = "true";
        surface.dataset.bugenceIgnore = "true";
        surface.innerHTML = currentBaseline;

        const metaBar = document.createElement("div");
        metaBar.className = "bugence-editor-meta";
        const wordMetric = document.createElement("span");
        wordMetric.className = "bugence-editor-meta__item";
        const charMetric = document.createElement("span");
        charMetric.className = "bugence-editor-meta__item";
        const readMetric = document.createElement("span");
        readMetric.className = "bugence-editor-meta__pill";
        metaBar.append(wordMetric, charMetric, readMetric);

        const linkPanel = document.createElement("div");
        linkPanel.className = "bugence-editor-link-panel";
        linkPanel.innerHTML = `<div class="bugence-editor-link-panel__label">Link settings</div>`;
        const linkField = document.createElement("label");
        linkField.className = "bugence-editor-link-panel__field";
        const linkSpan = document.createElement("span");
        linkSpan.textContent = "Href";
        const linkInput = document.createElement("input");
        linkInput.type = "url";
        linkInput.placeholder = "https://example.com";
        linkInput.value = baselineHref;
        linkInput.className = "bugence-editor-input";
        linkField.append(linkSpan, linkInput);

        const targetToggle = document.createElement("label");
        targetToggle.className = "bugence-editor-link-panel__toggle";
        const targetCheckbox = document.createElement("input");
        targetCheckbox.type = "checkbox";
        targetCheckbox.checked = baselineTargetBlank;
        const toggleText = document.createElement("span");
        toggleText.textContent = "Open in new tab";
        targetToggle.append(targetCheckbox, toggleText);

        linkPanel.append(linkField, targetToggle);

        const footer = document.createElement("footer");
        footer.className = "bugence-editor-modal__footer";
        footer.innerHTML = `
            <span class="bugence-editor-notice">Updates save to mission control. Publish to push live.</span>
        `;

        const actions = document.createElement("div");
        actions.className = "bugence-editor-actions";

        const toolbar = buildToolbar(surface);
        const updateMetaBar = () => {
            const text = (surface.innerText || "").trim();
            const words = text.length ? text.split(/\s+/).filter(Boolean).length : 0;
            const chars = text.replace(/\s+/g, " ").length;
            const readingSeconds = Math.max(10, Math.round((words / 200) * 60));
            wordMetric.textContent = `${words} words`;
            charMetric.textContent = `${chars} chars`;
            readMetric.textContent = `${Math.round(readingSeconds / 60) || 1} min read`;
        };
        updateMetaBar();

        body.appendChild(linkPanel);
        body.appendChild(metaBar);
        body.appendChild(toolbar);
        body.appendChild(surface);

        const updateToolbarStates = () => {
            if (typeof toolbar.updateStates === "function") {
                toolbar.updateStates();
            }
        };

        surface.addEventListener("keyup", updateToolbarStates);
        surface.addEventListener("mouseup", updateToolbarStates);

        const selectionHandler = () => {
            if (!document.contains(surface)) {
                document.removeEventListener("selectionchange", selectionHandler);
                return;
            }

            if (typeof toolbar.handleSelectionChange === "function") {
                toolbar.handleSelectionChange();
            } else {
                updateToolbarStates();
            }
        };

        document.addEventListener("selectionchange", selectionHandler);
        state.selectionHandler = selectionHandler;

        updateToolbarStates();

        const cancel = document.createElement("button");
        cancel.type = "button";
        cancel.className = "bugence-editor-btn bugence-editor-btn--ghost";
        cancel.textContent = "Cancel";
        cancel.addEventListener("click", () => closeOverlay());

        const revert = document.createElement("button");
        revert.type = "button";
        revert.className = "bugence-editor-btn bugence-editor-btn--revert";
        revert.textContent = "Revert change";

        const save = document.createElement("button");
        save.type = "button";
        save.className = "bugence-editor-btn bugence-editor-btn--primary";
        save.textContent = "Save changes";

        const normalizeLinkHref = (value) => {
            const trimmed = (value || "").trim();
            if (!trimmed) {
                return "";
            }
            if (/^(?:https?:|mailto:|tel:|sms:|ftp:|#|\/|\?)/i.test(trimmed)) {
                return trimmed;
            }
            return `https://${trimmed.replace(/^\/+/, "")}`;
        };
        const applyLinkToHtml = (html) => {
            const wrapper = document.createElement("div");
            wrapper.innerHTML = html || "";
            let anchor = wrapper.querySelector("a");
            const hrefVal = normalizeLinkHref(linkInput.value || "");

            if (hrefVal.length > 0) {
                if (!anchor) {
                    anchor = document.createElement("a");
                    anchor.innerHTML = wrapper.innerHTML?.trim() ? wrapper.innerHTML : surface.textContent || "";
                    wrapper.innerHTML = "";
                    wrapper.appendChild(anchor);
                }
                anchor.setAttribute("href", hrefVal);
                if (targetCheckbox.checked) {
                    anchor.setAttribute("target", "_blank");
                } else {
                    anchor.removeAttribute("target");
                }
            } else if (anchor) {
                anchor.removeAttribute("href");
                anchor.removeAttribute("target");
            }

            return wrapper.innerHTML;
        };

        save.addEventListener("click", async () => {
            const html = surface.innerHTML.trim();
            const htmlWithLink = applyLinkToHtml(html);

            const form = new FormData();
            if (section?.id) {
                form.append("SectionId", section.id);
            }

            const selector = section?.cssSelector ?? computeSelector(element);
            if (selector) {
                form.append("Selector", selector);
            }

            form.append("ContentType", "RichText");
            form.append("ContentValue", htmlWithLink);

            try {
                save.disabled = true;
                revert.disabled = true;
                cancel.disabled = true;
                const result = await saveSection(element, form);
                if (result?.section?.contentValue) {
                    element.innerHTML = result.section.contentValue;
                    currentBaseline = result.section.contentValue;
                } else {
                    element.innerHTML = htmlWithLink;
                    currentBaseline = htmlWithLink;
                }

                const liveAnchor = anchorTarget || element.querySelector("a");
                if (liveAnchor) {
                    const hrefVal = normalizeLinkHref(linkInput.value || "");
                    if (hrefVal.length > 0) {
                        liveAnchor.setAttribute("href", hrefVal);
                    }
                    if (targetCheckbox.checked) {
                        liveAnchor.setAttribute("target", "_blank");
                    } else {
                        liveAnchor.removeAttribute("target");
                    }
                }

                closeOverlay();
                showToast("Content updated.");
            } catch (error) {
                console.error(error);
                showToast(error.message ?? "Unable to save content.", "error");
            } finally {
                if (state.overlay) {
                    save.disabled = false;
                    cancel.disabled = false;
                    syncDirtyState();
                }
            }
        });

        const syncDirtyState = () => {
            const normalizedCurrent = normalizeHtml(surface.innerHTML);
            const normalizedBaseline = normalizeHtml(currentBaseline);
            const hrefDirty = (linkInput.value || "").trim() !== baselineHref.trim();
            const targetDirty = targetCheckbox.checked !== baselineTargetBlank;
            const isDirty = normalizedCurrent !== normalizedBaseline || hrefDirty || targetDirty;

            revert.disabled = !isDirty && !hasPreviousRevision;
            save.disabled = !isDirty;
        };

        revert.addEventListener("click", () => {
            const normalizedCurrent = normalizeHtml(surface.innerHTML);
            const normalizedBaseline = normalizeHtml(currentBaseline);

            if (normalizedCurrent !== normalizedBaseline) {
                surface.innerHTML = currentBaseline;
                element.innerHTML = currentBaseline;
            } else if (hasPreviousRevision && previousRevision) {
                surface.innerHTML = previousRevision;
                element.innerHTML = previousRevision;
            }

            linkInput.value = baselineHref;
            targetCheckbox.checked = baselineTargetBlank;
            updateToolbarStates();
            syncDirtyState();
            updateMetaBar();
        });

        surface.addEventListener("input", () => {
            syncDirtyState();
            updateMetaBar();
        });
        linkInput.addEventListener("input", syncDirtyState);
        targetCheckbox.addEventListener("change", syncDirtyState);

        const linkHint = document.createElement("p");
        linkHint.className = "bugence-editor-link-panel__hint";
        linkHint.textContent = "Editing text updates content. Editing link updates href on save/publish.";
        body.appendChild(linkHint);

        syncDirtyState();

        actions.appendChild(revert);
        actions.appendChild(cancel);
        actions.appendChild(save);
        footer.appendChild(actions);

        modal.appendChild(header);
        modal.appendChild(body);
        modal.appendChild(footer);

        overlay.appendChild(modal);
        brandTree(modal);
        document.body.appendChild(overlay);
    };

    const openImageEditor = (element, section) => {
        const overlay = ensureOverlay();
        overlay.innerHTML = "";

        const modal = document.createElement("article");
        modal.className = "bugence-editor-modal";
        modal.dataset.bugenceIgnore = "true";

        const header = document.createElement("header");
        header.className = "bugence-editor-modal__header";

        const titleWrap = document.createElement("div");
        const title = document.createElement("div");
        title.className = "bugence-editor-modal__title";
        title.textContent = section?.title ?? "Update image";
        const subtitle = document.createElement("p");
        subtitle.className = "bugence-editor-modal__subtitle";
        subtitle.textContent = `${config.pageName ?? "Page"} · Image`;
        titleWrap.append(title, subtitle);

        const closeBtn = document.createElement("button");
        closeBtn.type = "button";
        closeBtn.className = "bugence-editor-modal__close";
        closeBtn.setAttribute("aria-label", "Close editor");
        closeBtn.innerHTML = "&times;";
        closeBtn.addEventListener("click", () => closeOverlay());

        header.append(titleWrap, closeBtn);

        const body = document.createElement("div");
        body.className = "bugence-editor-modal__body";

        const preview = document.createElement("img");
        preview.className = "bugence-editor-image__preview";
        preview.alt = "Selected image";
        preview.dataset.bugenceIgnore = "true";

        const uploadRow = document.createElement("div");
        uploadRow.className = "bugence-editor-image__row";

        const fileInput = document.createElement("input");
        fileInput.type = "file";
        fileInput.accept = "image/*";
        fileInput.className = "bugence-editor-image__file";
        fileInput.dataset.bugenceIgnore = "true";

        const fileLabel = document.createElement("label");
        fileLabel.className = "bugence-editor-image__label";
        fileLabel.textContent = "Choose image";
        fileLabel.appendChild(fileInput);

        uploadRow.append(preview, fileLabel);

        const altWrapper = document.createElement("div");
        altWrapper.className = "bugence-editor-image__alt";
        const altLabel = document.createElement("label");
        altLabel.setAttribute("for", "bugence-editor-alt");
        altLabel.textContent = "Alt text";
        const altInput = document.createElement("input");
        altInput.type = "text";
        altInput.id = "bugence-editor-alt";
        altInput.className = "bugence-editor-image__alt-input";
        altInput.placeholder = "Describe this image for accessibility";
        const altHint = document.createElement("p");
        altHint.className = "bugence-editor-helper";
        altHint.textContent = "Aim for 5-15 words that describe the subject and context.";
        const altMeter = document.createElement("div");
        altMeter.className = "bugence-editor-meter";
        const altMeterFill = document.createElement("span");
        altMeterFill.className = "bugence-editor-meter__fill";
        altMeter.appendChild(altMeterFill);
        altWrapper.append(altLabel, altInput, altHint, altMeter);

        body.append(uploadRow, altWrapper);

        const footer = document.createElement("footer");
        footer.className = "bugence-editor-modal__footer";
        footer.innerHTML = `
            <span class="bugence-editor-notice">Updates save to mission control. Publish to push live.</span>
        `;

        const actions = document.createElement("div");
        actions.className = "bugence-editor-actions";

        let baselineSrc = section?.mediaPath ?? (element instanceof HTMLImageElement ? element.src : "") ?? "";
        if (baselineSrc) {
            preview.src = baselineSrc;
        }

        let baselineAlt = (section?.mediaAltText ?? (element instanceof HTMLImageElement ? element.alt : "") ?? "").trim();
        altInput.value = baselineAlt;

        const validateFile = (file) => {
            const maxSizeMb = 8;
            const allowed = /^image\\//i.test(file.type);
            if (!allowed) {
                showToast("Only image files are allowed.", "error");
                return false;
            }
            if (file.size > maxSizeMb * 1024 * 1024) {
                showToast(`Image must be under ${maxSizeMb}MB.`, "error");
                return false;
            }
            return true;
        };

        const syncAltMeter = () => {
            const len = altInput.value.trim().length;
            const pct = Math.min(100, Math.max(10, (len / 120) * 100));
            altMeterFill.style.width = `${pct}%`;
            altMeterFill.dataset.state = len >= 5 ? "good" : "poor";
        };

        const cancel = document.createElement("button");
        cancel.type = "button";
        cancel.className = "bugence-editor-btn bugence-editor-btn--ghost";
        cancel.textContent = "Cancel";
        cancel.addEventListener("click", () => closeOverlay());

        const revert = document.createElement("button");
        revert.type = "button";
        revert.className = "bugence-editor-btn bugence-editor-btn--revert";
        revert.textContent = "Revert";

        const save = document.createElement("button");
        save.type = "button";
        save.className = "bugence-editor-btn bugence-editor-btn--primary";
        save.textContent = "Save image";

        const syncDirtyState = () => {
            const hasFile = Boolean(fileInput.files && fileInput.files.length);
            const altDirty = altInput.value.trim() !== baselineAlt;
            const imageDirty = preview.src !== baselineSrc;
            const dirty = hasFile || altDirty || imageDirty;
            revert.disabled = !dirty;
            save.disabled = !dirty;
        };

        fileInput.addEventListener("change", () => {
            const [file] = fileInput.files ?? [];
            if (!file) {
                preview.src = baselineSrc;
                syncDirtyState();
                return;
            }

            if (!validateFile(file)) {
                fileInput.value = "";
                preview.src = baselineSrc;
                syncDirtyState();
                return;
            }

            const reader = new FileReader();
            reader.onload = (e) => {
                const nextSrc = typeof e.target?.result === "string" ? e.target.result : "";
                if (nextSrc) {
                    preview.src = nextSrc;
                }
                syncDirtyState();
            };
            reader.readAsDataURL(file);
        });

        altInput.addEventListener("input", () => {
            syncAltMeter();
            syncDirtyState();
        });
        syncAltMeter();

        save.addEventListener("click", async () => {
            const selectedFile = fileInput.files && fileInput.files[0];
            const altValue = altInput.value.trim();

            if (selectedFile && !validateFile(selectedFile)) {
                fileInput.value = "";
                preview.src = baselineSrc;
                syncDirtyState();
                return;
            }

            if (!selectedFile && !baselineSrc) {
                showToast("Please choose an image.", "error");
                return;
            }

            const form = new FormData();
            if (section?.id) {
                form.append("SectionId", section.id);
            }

            const selector = section?.cssSelector ?? computeSelector(element);
            if (selector) {
                form.append("Selector", selector);
            }

            if (selectedFile) {
                form.append("Image", selectedFile);
            }

            form.append("ContentType", "Image");
            form.append("ContentValue", "");
            form.append("MediaAltText", altValue);

            try {
                save.disabled = true;
                revert.disabled = true;
                cancel.disabled = true;
                const result = await saveSection(element, form);
                if (result?.section?.mediaPath) {
                    preview.src = result.section.mediaPath;
                    baselineSrc = result.section.mediaPath;
                } else if (selectedFile) {
                    baselineSrc = preview.src;
                }

                baselineAlt = altValue;
                if (element instanceof HTMLImageElement) {
                    element.src = baselineSrc;
                    element.alt = baselineAlt;
                }

                closeOverlay();
                showToast("Image updated.");
            } catch (error) {
                console.error(error);
                showToast(error.message ?? "Unable to save image.", "error");
            } finally {
                if (state.overlay) {
                    save.disabled = false;
                    revert.disabled = false;
                    cancel.disabled = false;
                    syncDirtyState();
                }
            }
        });

        revert.addEventListener("click", () => {
            preview.src = baselineSrc;
            if (element instanceof HTMLImageElement) {
                element.src = baselineSrc;
            }
            altInput.value = baselineAlt;
            fileInput.value = "";
            syncAltMeter();
            syncDirtyState();
        });

        syncDirtyState();

        actions.append(revert, cancel, save);
        footer.appendChild(actions);
        modal.append(header, body, footer);

        overlay.appendChild(modal);
        brandTree(modal);
        document.body.appendChild(overlay);
    };
    const openEditor = (element) => {
        const section = getSectionForElement(element);
        if (isLocked(section) || shouldLockElement(element)) {
            showToast("This block is locked and cannot be edited.", "error");
            return;
        }

        if (isImageElement(element)) {
            openImageEditor(element, section);
            return;
        }

        openTextEditor(element, section);
    };

    const handleDoubleClick = (event) => {
        if (state.overlay && event.target instanceof Node && state.overlay.contains(event.target)) {
            return;
        }
        const candidate = sanitizeTarget(event.target);
        if (!candidate) {
            return;
        }

        event.preventDefault();
        event.stopPropagation();
        openEditor(candidate);
    };

    const bootstrap = async () => {
        document.body?.classList.add("bugence-editor-active", "bugence-visual-editor-active");
        document.documentElement?.classList.add("bugence-editor-active", "bugence-visual-editor-active");

        const sidebarRefs = ensureSidebar();
        sidebarRefs.root.dataset.bugenceDraft = "false";
        sidebarRefs.statusLabel.textContent = "Loading content";
        sidebarRefs.statusMeta.textContent = "Fetching editable sections.";
        sidebarRefs.publishButton.disabled = true;
        delete sidebarRefs.publishButton.dataset.state;

        try {
            const response = await fetchWithTimeout(PAGE_ENDPOINT, {
                credentials: "include",
                timeoutMs: 12000
            });

            if (!response.ok) {
                throw new Error(`Unable to load editable sections (HTTP ${response.status}).`);
            }

            const data = await response.json();
            state.page = data?.page ?? null;

            state.sectionsById.clear();
            state.sectionsByKey.clear();
            state.sectionsBySelector.clear();
            state.elementSections = new WeakMap();

            if (Array.isArray(data?.sections)) {
                registerSections(data.sections);
            } else {
                refreshSidebar();
            }

            document.addEventListener("mouseover", handleHover, true);
            document.addEventListener("mouseout", handleMouseOut, true);
            document.addEventListener("dblclick", handleDoubleClick, true);
        } catch (error) {
            console.error("[Bugence Editor] Failed to initialize", error);
            state.page = null;
            showToast(error instanceof Error && error.message ? error.message : "Visual editor failed to load.", "error");
            const fallbackSidebar = ensureSidebar();
            fallbackSidebar.publishButton.disabled = true;
            fallbackSidebar.statusLabel.textContent = "Editor unavailable";
            fallbackSidebar.statusMeta.textContent = (error instanceof Error && error.message) || "Unable to load editable sections.";
        }
    };

    if (document.readyState === "complete" || document.readyState === "interactive") {
        window.setTimeout(bootstrap, 0);
    } else {
        document.addEventListener("DOMContentLoaded", bootstrap);
    }
})();
