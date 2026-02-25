// @ts-nocheck
import type {
    ContentPageResponse,
    DeleteSectionResponse,
    EditorConfig,
    PublishResponse,
    SectionFieldSchema,
    SectionMutationResponse
} from "@bugence/core";
import { resolveSectionDescriptor, sectionSchemas } from "@bugence/core";
import { renderSectionFields, TextFieldRenderResult, ImageFieldRenderResult } from "./renderers/sectionFields";

(async () => {
    const extractPageIdFromPath = (): string | null => {
        const match = window.location.pathname.match(
            /([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})/i
        );
        return match ? match[1] : null;
    };

    const fetchConfigFromApi = async (pageId: string): Promise<EditorConfig | null> => {
        try {
            const response = await fetch(`/api/content/pages/${pageId}`, { credentials: "include" });
            if (!response.ok) {
                return null;
            }

            const payload = await response.json();
            const page = (payload?.page ?? payload?.Page) as any;
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
                editUrl: normalizedId ? `/Content/Edit/${normalizedId}` : undefined
            };
        } catch (error) {
            console.warn("[bugence Editor] Unable to hydrate configuration from API.", error);
            return null;
        }
    };

    const resolveConfig = async (): Promise<EditorConfig | null> => {
        if (window.__bugenceEditor && typeof window.__bugenceEditor === "object") {
            return window.__bugenceEditor;
        }

        const configScript = document.getElementById("bugence-editor-config");
        if (configScript instanceof HTMLScriptElement) {
            const rawPayload = (configScript.textContent ?? configScript.innerHTML ?? "").trim();
            if (rawPayload.length) {
                try {
                    const parsed = JSON.parse(rawPayload);
                    window.__bugenceEditor = parsed;
                    return parsed;
                } catch (error) {
                    console.error("[bugence Editor] Unable to parse configuration payload.", error);
                    // Fall through to API fallback below.
                }
            }
        }

        const fallbackId = extractPageIdFromPath();
        if (fallbackId) {
            const hydrated = await fetchConfigFromApi(fallbackId);
            if (hydrated) {
                window.__bugenceEditor = hydrated;
                return hydrated;
            }

            const minimal: EditorConfig = { pageId: fallbackId, apiBase: "/api/content", selectorHints: {} };
            window.__bugenceEditor = minimal;
            return minimal;
        }

        return null;
    };

    const config = await resolveConfig();
    if (!config || !config.pageId) {
        console.warn("[bugence Editor] Missing configuration payload.");
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
        "site-menu",
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
        "menu-bar"
    ];
    const LOCK_DATA_FLAGS = new Set(["true", "1", "locked"]);

    const shouldLockElement = (element: Element | null | undefined): boolean => {
        if (!element) {
            return false;
        }

        let current: Element | null = element;
        const baseCandidate = current;

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

    const fetchWithTimeout = async (input: RequestInfo | URL, init?: RequestInit & { timeoutMs?: number }) => {
        const timeout = init?.timeoutMs ?? 12000;
        const controller = new AbortController();
        const id = window.setTimeout(() => controller.abort(), timeout);
        try {
            return await fetch(input, { ...init, signal: controller.signal });
        } finally {
            window.clearTimeout(id);
        }
    };

    const ALLOWED_STYLE_PROPERTIES = new Set([
        "color",
        "background-color",
        "font-size",
        "font-family",
        "font-style",
        "font-weight",
        "font-variant",
        "text-transform",
        "text-decoration",
        "text-decoration-line",
        "letter-spacing",
        "line-height",
        "text-align"
    ]);

    const FONT_SIZE_MAP = new Map<string, string>([
        ["1", "12px"],
        ["2", "14px"],
        ["3", "16px"],
        ["4", "18px"],
        ["5", "22px"],
        ["6", "28px"],
        ["7", "36px"]
    ]);

    const FONT_SIZE_VALUE_TO_COMMAND = new Map<string, string>(
        Array.from(FONT_SIZE_MAP.entries()).map(([command, size]) => [size, command])
    );

    const FONT_SIZE_KEYWORD_MAP = new Map<string, string>([
        ["xx-small", "12px"],
        ["x-small", "13px"],
        ["small", "14px"],
        ["medium", "16px"],
        ["large", "18px"],
        ["x-large", "22px"],
        ["xx-large", "36px"]
    ]);

    const FONT_FAMILY_MAP = new Map<string, string>([
        ["Inter", '"Inter", "Segoe UI", sans-serif'],
        ["Georgia", '"Georgia", "Times New Roman", serif'],
        ["Playfair Display", '"Playfair Display", "Times New Roman", serif'],
        ["Space Grotesk", '"Space Grotesk", "Inter", sans-serif'],
        ["Roboto Mono", '"Roboto Mono", "Menlo", monospace'],
        ["Mono", '"Roboto Mono", "Menlo", monospace'],
        ["Work Sans", '"Work Sans", "Inter", sans-serif'],
        ["Merriweather", '"Merriweather", "Georgia", serif'],
        ["Bebas Neue", '"Bebas Neue", "Impact", "Arial Black", sans-serif'],
        ["Oswald", '"Oswald", "Arial Narrow", sans-serif']
    ]);

    const FONT_FAMILY_OPTIONS: Array<{ label: string; value: string }> = [
        { label: "Auto (Theme)", value: "" },
        { label: "Inter · Default", value: "Inter" },
        { label: "Work Sans · Modern", value: "Work Sans" },
        { label: "Space Grotesk · Tech", value: "Space Grotesk" },
        { label: "Georgia · Classic Serif", value: "Georgia" },
        { label: "Playfair Display · Editorial", value: "Playfair Display" },
        { label: "Merriweather · Reading Serif", value: "Merriweather" },
        { label: "Roboto Mono · Mono", value: "Roboto Mono" },
        { label: "Bebas Neue · Impact", value: "Bebas Neue" },
        { label: "Oswald · Condensed", value: "Oswald" }
    ];

    const FONT_SIZE_OPTIONS: Array<{ label: string; value: string }> = [
        { label: "XS · 12px", value: "1" },
        { label: "S · 14px", value: "2" },
        { label: "Base · 16px", value: "3" },
        { label: "M · 18px", value: "4" },
        { label: "L · 22px", value: "5" },
        { label: "XL · 28px", value: "6" },
        { label: "Display · 36px", value: "7" }
    ];

    const normalizeFontSizeValue = (value: string | null | undefined): string | null => {
        if (!value) {
            return null;
        }

        const trimmed = value.trim().toLowerCase();
        if (FONT_SIZE_MAP.has(trimmed)) {
            return FONT_SIZE_MAP.get(trimmed) ?? null;
        }

        if (FONT_SIZE_KEYWORD_MAP.has(trimmed)) {
            return FONT_SIZE_KEYWORD_MAP.get(trimmed) ?? null;
        }

        if (/^\d+(\.\d+)?px$/.test(trimmed)) {
            return trimmed;
        }

        if (/^\d+(\.\d+)?rem$/.test(trimmed)) {
            const numeric = Number.parseFloat(trimmed.replace("rem", ""));
            if (Number.isFinite(numeric)) {
                return `${Math.round(numeric * 16)}px`;
            }
        }

        if (/^\d+(\.\d+)?em$/.test(trimmed)) {
            const numeric = Number.parseFloat(trimmed.replace("em", ""));
            if (Number.isFinite(numeric)) {
                return `${Math.round(numeric * 16)}px`;
            }
        }

        return null;
    };

    const normalizeFontFamilyValue = (value: string | null | undefined): string | null => {
        if (!value) {
            return null;
        }

        const normalized = value.trim().replace(/^["']+|["']+$/g, "");
        if (!normalized.length) {
            return null;
        }

        if (FONT_FAMILY_MAP.has(normalized)) {
            return FONT_FAMILY_MAP.get(normalized) ?? null;
        }

        return normalized;
    };

    const resolveFontFamilyKey = (value: string | null | undefined): string | null => {
        if (!value) {
            return null;
        }

        const firstToken = value.split(",")[0]?.replace(/^["']+|["']+$/g, "").trim();
        if (!firstToken?.length) {
            return null;
        }

        for (const key of FONT_FAMILY_MAP.keys()) {
            if (key.toLowerCase() === firstToken.toLowerCase()) {
                return key;
            }
        }

        return firstToken;
    };

    const normalizeFontElements = (root: ParentNode | null | undefined) => {
        if (!root) {
            return;
        }

        const convertFontElement = (font: HTMLElement) => {
            const span = document.createElement("span");
            span.dataset.bugenceInline = "font";

            const colorAttr = (font.getAttribute("color") ?? font.style.color) ?? "";
            const familyAttr = (font.getAttribute("face") ?? font.style.fontFamily) ?? "";
            const sizeAttr = (font.getAttribute("size") ?? font.style.fontSize) ?? "";

            const color = colorAttr.trim();
            if (color.length) {
                span.style.color = color;
            }

            const family = normalizeFontFamilyValue(familyAttr);
            if (family) {
                span.style.fontFamily = family;
            }

            const fontSize = normalizeFontSizeValue(sizeAttr);
            if (fontSize) {
                span.style.fontSize = fontSize;
            }

            while (font.firstChild) {
                span.appendChild(font.firstChild);
            }
            font.replaceWith(span);
        };

        root.querySelectorAll("font").forEach((font) => {
            convertFontElement(font as HTMLElement);
        });
    };

    const resolveSectionKey = (section: any): string => {
        if (!section) {
            return "";
        }

        const key = typeof section.sectionKey === "string"
            ? section.sectionKey
            : typeof section.SectionKey === "string"
                ? section.SectionKey
                : undefined;

        return key ? key.trim() : "";
    };

    const resolveSectionKeyFromElement = (element: Element | null | undefined): string => {
        if (!element || !(element instanceof HTMLElement)) {
            return "";
        }

        return (
            element.dataset.bugenceSection ||
            element.dataset.bugenceSectionKey ||
            element.getAttribute("data-bugence-section") ||
            ""
        ).trim();
    };

    const resolveDescriptorForContext = (section: any, element: Element | null | undefined) => {
        const key = resolveSectionKey(section) || resolveSectionKeyFromElement(element);
        if (!key) {
            return null;
        }

        return resolveSectionDescriptor(sectionSchemas, key);
    };

    const escapeHtml = (value) => {
        if (typeof value !== "string") {
            return "";
        }

        return value
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#39;");
    };

    const sanitizeStyleAttribute = (value) => {
        if (!value) {
            return "";
        }

        const sanitized = value
            .split(";")
            .map((segment) => segment.trim())
            .filter(Boolean)
            .map((segment) => {
                const [rawProperty, ...rawValueParts] = segment.split(":");
                if (!rawProperty || rawValueParts.length === 0) {
                    return null;
                }

                const property = rawProperty.trim().toLowerCase();
                if (!ALLOWED_STYLE_PROPERTIES.has(property)) {
                    return null;
                }

                const rawValue = rawValueParts.join(":").trim();
                if (!rawValue || /url\s*\(/i.test(rawValue) || /expression/i.test(rawValue) || /javascript:/i.test(rawValue)) {
                    return null;
                }

                return `${property}: ${rawValue}`;
            })
            .filter(Boolean)
            .join("; ");

        return sanitized;
    };

    const sanitizeRichHtml = (input) => {
        const template = document.createElement("template");
        template.innerHTML = input ?? "";

        template.content.querySelectorAll("script, style").forEach((node) => node.remove());
        template.content.querySelectorAll("*").forEach((node) => {
            Array.from(node.attributes).forEach((attr) => {
                if (attr.name.startsWith("on")) {
                    node.removeAttribute(attr.name);
                    return;
                }

                if (attr.name === "style") {
                    const cleaned = sanitizeStyleAttribute(attr.value);
                    if (cleaned) {
                        node.setAttribute("style", cleaned);
                    } else {
                        node.removeAttribute("style");
                    }
                }
            });
        });

        normalizeFontElements(template.content);

        return template.innerHTML;
    };

    const normalizePlainText = (value) => {
        if (typeof value !== "string") {
            return "";
        }

        return value
            .replace(/\u00a0/g, " ")
            .replace(/\r/g, "")
            .replace(/\n{3,}/g, "\n\n")
            .trim();
    };

    const plainTextToHtml = (plain) => {
        if (!plain) {
            return "";
        }

        return escapeHtml(plain).replace(/(?:\r\n|\r|\n)/g, "<br>");
    };

    const extractPlainTextFromHtml = (html) => {
        if (!html) {
            return "";
        }

        const template = document.createElement("template");
        template.innerHTML = html;
        return normalizePlainText(template.content.textContent ?? "");
    };

    const normalizeHtml = (value) =>
        (value ?? "")
            .replace(/&nbsp;/g, " ")
            .replace(/\s+/g, " ")
            .trim();

    const detectSectionMode = (section) => {
        const type = (section?.contentType ?? section?.ContentType ?? "").toString().toLowerCase();
        if (type === "text") {
            return "Text";
        }
        if (type === "html") {
            return "Html";
        }
        return "RichText";
    };

    const buildSnapshot = (value, mode) => {
        const canonical = mode === "Text" ? "Text" : mode === "Html" ? "Html" : "RichText";

        if (canonical === "Text") {
            const plain = normalizePlainText(value ?? "");
            return {
                mode: "Text",
                plain,
                html: plainTextToHtml(plain),
                storageValue: plain
            };
        }

        const sanitized = sanitizeRichHtml(value ?? "");
        return {
            mode: canonical,
            plain: extractPlainTextFromHtml(sanitized),
            html: sanitized,
            storageValue: sanitized
        };
    };

    const snapshotsEqual = (a, b) => {
        if (!a || !b || a.mode !== b.mode) {
            return false;
        }

        if (a.mode === "Text") {
            return a.plain === b.plain;
        }

        return normalizeHtml(a.html) === normalizeHtml(b.html);
    };

    const state = {
        sectionsById: new Map(),
        sectionsByKey: new Map(),
        sectionsBySelector: new Map(),
        elementSections: new WeakMap(),
        badge: null,
        badgeLockedTag: null,
        activeElement: null,
        activeSection: null,
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
            console.warn("[bugence Editor] Unable to format timestamp", error);
            return date.toISOString();
        }
    };

    const recomputeSectionMetrics = () => {
        let lastPublished = null;
        let lastUpdated = null;
        let hasDrafts = false;
        let lockedCount = 0;

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

            if (isLocked(section)) {
                lockedCount += 1;
            }
        });

        state.lastPublishedAt = lastPublished;
        state.lastUpdatedAt = lastUpdated;
        state.hasDraftChanges = hasDrafts;
        state.lockedCount = lockedCount;
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

        const sectionsValue = createStat("Editable sections", "🧩");
        const draftValue = createStat("Pending edits", "📝");
        const lockedValue = createStat("Locked sections", "🔒");
        const publishValue = createStat("Last publish", "🕒");

        body.append(stats);

        const insightsList = document.createElement("ul");
        insightsList.className = "bugence-editor-sidebar__insights";
        body.append(insightsList);

        const timelineSection = document.createElement("section");
        timelineSection.className = "bugence-editor-sidebar__timeline";

        const timelineTitle = document.createElement("p");
        timelineTitle.className = "bugence-editor-sidebar__timeline-title";
        timelineTitle.textContent = "Latest activity";

        const timelineList = document.createElement("ul");
        timelineList.className = "bugence-editor-sidebar__activity";

        timelineSection.append(timelineTitle, timelineList);
        body.append(timelineSection);

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

        state.sidebar = {
            root: sidebar,
            title,
            slug,
            statusLabel,
            statusMeta,
            statusDot,
            sectionsValue,
            draftValue,
            lockedValue,
            publishValue,
            publishButton,
            publishLabel,
            liveLink,
            dashboardLink,
            insightsList,
            timelineList
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

        if (sidebarRefs.lockedValue) {
            sidebarRefs.lockedValue.textContent = hasSections ? String(state.lockedCount ?? 0) : "0";
        }

        if (sidebarRefs.insightsList) {
            const insights = sidebarRefs.insightsList;
            insights.innerHTML = "";

            const insightItems = [];
            const unlockedCount = Math.max(0, state.sectionsById.size - (state.lockedCount ?? 0));
            insightItems.push({
                icon: state.hasDraftChanges ? "⚠️" : "✅",
                label: state.hasDraftChanges ? "Drafts waiting" : "Live in sync",
                meta: state.hasDraftChanges
                    ? "Publish to push pending updates"
                    : "All edits are published"
            });
            insightItems.push({
                icon: "✏️",
                label: "Editable sections",
                meta: `${unlockedCount} open · ${state.lockedCount ?? 0} locked`
            });

            if (state.lastUpdatedAt) {
                insightItems.push({
                    icon: "🕑",
                    label: "Last editor touch",
                    meta: formatTimestamp(state.lastUpdatedAt)
                });
            }

            insightItems.forEach((entry) => {
                const li = document.createElement("li");
                li.className = "bugence-editor-sidebar__insight";
                li.innerHTML = `<span class="bugence-editor-sidebar__insight-icon">${entry.icon}</span>
                    <div>
                        <strong>${entry.label}</strong>
                        <span>${entry.meta}</span>
                    </div>`;
                insights.append(li);
            });
        }

        if (sidebarRefs.timelineList) {
            const timeline = sidebarRefs.timelineList;
            timeline.innerHTML = "";

            const activityItems = [];
            if (state.lastUpdatedAt) {
                activityItems.push({
                    label: "Last edit",
                    meta: formatTimestamp(state.lastUpdatedAt),
                    icon: "✏️"
                });
            }
            if (state.lastPublishedAt) {
                activityItems.push({
                    label: "Last publish",
                    meta: formatTimestamp(state.lastPublishedAt),
                    icon: "🚀"
                });
            }
            if (state.sectionsById.size > 0) {
                const unlocked = Math.max(0, state.sectionsById.size - (state.lockedCount ?? 0));
                activityItems.push({
                    label: "Unlocked sections",
                    meta: `${unlocked} ready to edit`,
                    icon: "🔓"
                });
            }

            if (activityItems.length === 0) {
                const empty = document.createElement("li");
                empty.className = "bugence-editor-sidebar__activity-item";
                empty.innerHTML = `<span class="bugence-editor-sidebar__activity-icon">ℹ️</span>
                    <div><strong>No activity captured</strong><span>Changes will appear here as you edit.</span></div>`;
                timeline.append(empty);
            } else {
                activityItems.forEach((entry) => {
                    const item = document.createElement("li");
                    item.className = "bugence-editor-sidebar__activity-item";
                    item.innerHTML = `<span class="bugence-editor-sidebar__activity-icon">${entry.icon}</span>
                        <div><strong>${entry.label}</strong><span>${entry.meta}</span></div>`;
                    timeline.append(item);
                });
            }
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
        state.activeSection = null;
    };

    const positionBadge = (element, section) => {
        const badge = ensureBadge();
        const rect = element.getBoundingClientRect();
        const top = window.scrollY + rect.top + 4;
        const left = window.scrollX + rect.right - 4;

        badge.style.top = `${top}px`;
        badge.style.left = `${left}px`;
        badge.dataset.state = isLocked(section) ? "locked" : "ready";
        badge.classList.add("bugence-editor-badge--visible");
        badge.style.display = "inline-flex";

        if (state.badgeLockedTag) {
            state.badgeLockedTag.hidden = !isLocked(section);
        }
    };

    const sanitizeTarget = (target) => {
        if (!target) {
            return null;
        }

        let current: Element | null;
        let baseCandidate: Element | null = null;

        if (target instanceof Element) {
            if (state.overlay && state.overlay.contains(target)) {
                return null;
            }
            if (target.dataset?.bugenceIgnore === "true") {
                return null;
            }

            const host = target.closest("[data-bugence-section]");
            if (host instanceof HTMLElement && host.dataset?.bugenceIgnore !== "true") {
                current = host;
                baseCandidate = host;
            } else {
                current = target;
                baseCandidate = target;
            }
        } else if (target instanceof Text) {
            current = target.parentElement;
            baseCandidate = current;
        } else {
            current = null;
        }

        while (current && current !== document.body) {
            if (!(current instanceof Element)) {
                current = current?.parentElement ?? null;
                continue;
            }

            if (BLOCKED_TAGS.has(current.tagName)) {
                return null;
            }

            if (current.dataset?.bugenceIgnore === "true") {
                return null;
            }

            if (current.dataset?.bugenceSection) {
                return current;
            }

            if (current.childElementCount === 0 || isImageElement(current)) {
                return current;
            }

            const meaningfulText = Array.from(current.childNodes)
                .filter((node) => node.nodeType === Node.TEXT_NODE)
                .map((node) => node.textContent?.trim() ?? "")
                .join("");

            if (meaningfulText.length >= 2) {
                return current;
            }

            const next = current.firstElementChild ?? current.nextElementSibling ?? current.parentElement;
            current = next;
        }

        if (current instanceof Element) {
            return current;
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

        const lockedByLayout = shouldLockElement(element);
        if (lockedByLayout && !section.isLocked) {
            section.isLocked = true;
        }

        if (section.isLocked) {
            element.setAttribute("data-bugence-locked", "true");
        } else {
            element.removeAttribute("data-bugence-locked");
        }

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
                console.warn("[bugence Editor] Invalid selector for section", section.sectionKey, error);
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
                console.warn("[bugence Editor] Invalid selector hint", candidate, error);
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
                    console.warn("[bugence Editor] Stored selector is invalid", section.cssSelector, error);
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

    const getSectionContext = (element: Element | EventTarget | null | undefined) => {
        if (!element) {
            return null;
        }

        let base: Element | null =
            element instanceof Element
                ? element
                : (element as Node | null) instanceof Node
                    ? (element as Node).parentElement
                    : null;

        if (!base) {
            return null;
        }

        const visited = new Set<Element>();

        let current: Element | null = base;
        while (current && current !== document.body) {
            if (visited.has(current)) {
                break;
            }
            visited.add(current);

            if (current instanceof HTMLElement && current.dataset?.bugenceIgnore === "true") {
                return null;
            }

            if (state.elementSections.has(current)) {
                const mapped = state.elementSections.get(current);
                return mapped ? { section: mapped, element: current } : null;
            }

            const key = resolveSectionKeyFromElement(current);
            if (key && state.sectionsByKey.has(key)) {
                const section = state.sectionsByKey.get(key);
                if (section) {
                    state.elementSections.set(current, section);
                    return { section, element: current };
                }
            }

            const selector = computeSelector(current);
            if (selector && state.sectionsBySelector.has(selector)) {
                const section = state.sectionsBySelector.get(selector);
                if (section) {
                    state.elementSections.set(current, section);
                    return { section, element: current };
                }
            }

            const rootNode = (current as any).getRootNode?.();
            if (!current.parentElement && rootNode && rootNode instanceof ShadowRoot) {
                current = rootNode.host as Element;
                continue;
            }

            current = current.parentElement;
        }

        const fallbackSelector = computeSelector(base);
        if (fallbackSelector && state.sectionsBySelector.has(fallbackSelector)) {
            const section = state.sectionsBySelector.get(fallbackSelector);
            if (section) {
                if (base instanceof HTMLElement) {
                    state.elementSections.set(base, section);
                }
                return { section, element: base instanceof HTMLElement ? base : null };
            }
        }

        for (const hintKey of Object.keys(selectorHints)) {
            const hintSelector = resolveHintSelector(hintKey);
            if (!hintSelector) {
                continue;
            }

            try {
                if (base.matches(hintSelector)) {
                    const hintedSection = state.sectionsByKey.get(hintKey) ?? state.sectionsBySelector.get(hintSelector);
                    if (hintedSection) {
                        if (base instanceof HTMLElement) {
                            state.elementSections.set(base, hintedSection);
                        }
                        return { section: hintedSection, element: base instanceof HTMLElement ? base : null };
                    }
                }
            } catch (error) {
                console.warn("[bugence Editor] Failed to evaluate selector hint", hintSelector, error);
            }
        }

        return null;
    };

    const getSectionForElement = (element) => {
        return getSectionContext(element)?.section ?? null;
    };

    const handleHover = (event) => {
        if (state.overlay && event.target instanceof Node && state.overlay.contains(event.target)) {
            return;
        }
        const candidate = sanitizeTarget(event.target);
        if (!candidate) {
            hideBadge();
            return;
        }

        const context = getSectionContext(candidate);
        const highlightTarget = context?.element ?? (candidate instanceof HTMLElement ? candidate : null);

        if (highlightTarget && highlightTarget === state.activeElement) {
            return;
        }

        if (state.activeElement && state.activeElement !== highlightTarget) {
            state.activeElement.classList?.remove("bugence-editor-highlight");
            state.activeElement.classList?.remove("bugence-editor-highlight--locked");
        }

        const section = context?.section ?? null;
        state.activeSection = section ?? null;

        if (!highlightTarget || !section) {
            hideBadge();
            return;
        }

        if (isLocked(section)) {
            highlightTarget.classList?.add("bugence-editor-highlight--locked");
        } else {
            highlightTarget.classList?.remove("bugence-editor-highlight--locked");
        }

        highlightTarget.classList?.add("bugence-editor-highlight");
        state.activeElement = highlightTarget;
        positionBadge(highlightTarget, section);
    };

    const handleMouseOut = (event) => {
        if (!state.activeElement) {
            return;
        }

        const related = event.relatedTarget;
        const activeElement = state.activeElement;
        if (
            related &&
            (
                related === activeElement ||
                (activeElement instanceof HTMLElement && activeElement.contains(related as Node)) ||
                related === state.badge ||
                state.badge?.contains(related)
            )
        ) {
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
            normalizeFontElements(surface);
            return;
        }

        if (command === "unlink" || command === "undo" || command === "redo" || command === "insertHorizontalRule") {
            document.execCommand(command);
            normalizeFontElements(surface);
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
            normalizeFontElements(surface);

            try {
                document.execCommand("styleWithCSS", false, false);
            } catch (error) {
                // ignore
            }
            return;
        }

        if (command === "fontSize") {
            document.execCommand("styleWithCSS", false, true);
            document.execCommand("fontSize", false, value ?? "3");
            normalizeFontElements(surface);
            try {
                document.execCommand("styleWithCSS", false, false);
            } catch (error) {
                // ignore
            }
            return;
        }

        if (command === "fontName") {
            document.execCommand("styleWithCSS", false, true);
            document.execCommand("fontName", false, value ?? "Inter");
            normalizeFontElements(surface);
            try {
                document.execCommand("styleWithCSS", false, false);
            } catch (error) {
                // ignore
            }
            return;
        }

        document.execCommand(command, false, value ?? null);
        normalizeFontElements(surface);
    };

    type ToolbarButtonConfig = {
        label: string;
        command: string;
        token?: string;
        value?: string;
        title?: string;
        toggles?: boolean;
    };

    type ToolbarPaletteConfig = {
        label: string;
        command: string;
        token: string;
        swatches: Array<{ label: string; value: string; preview?: string }>;
        allowCustom?: boolean;
    };

    type ToolbarSelectOption = {
        label: string;
        value: string;
    };

    type ToolbarSelectConfig = {
        label: string;
        command: string;
        token: string;
        options: ToolbarSelectOption[];
    };

    type ToolbarOptions = {
        allowedTokens?: string[];
    };

    const buildToolbar = (surface: HTMLElement, options: ToolbarOptions = {}) => {
        const toolbar = document.createElement("div");
        toolbar.className = "bugence-editor-toolbar";

        const trackedButtons: Array<{ element: HTMLButtonElement; config: ToolbarButtonConfig }> = [];
        const trackedSelects: Array<{ element: HTMLSelectElement; config: ToolbarSelectConfig }> = [];

        const clearInlineStyle = (cssProperty: string) => {
            const selection = document.getSelection();
            if (!selection || selection.rangeCount === 0) {
                return;
            }

            const range = selection.getRangeAt(0);
            const host =
                range.commonAncestorContainer instanceof Element
                    ? range.commonAncestorContainer
                    : range.commonAncestorContainer?.parentElement;

            if (host && !surface.contains(host)) {
                return;
            }

            const candidates = Array.from(surface.querySelectorAll<HTMLElement>("*"));
            candidates.forEach((candidate) => {
                try {
                    if (!range.intersectsNode(candidate)) {
                        return;
                    }
                } catch (error) {
                    return;
                }

                if (!candidate.style || !candidate.style.getPropertyValue(cssProperty)) {
                    return;
                }

                candidate.style.removeProperty(cssProperty);
                if (!(candidate.getAttribute("style") ?? "").trim().length) {
                    candidate.removeAttribute("style");
                }
            });

            normalizeFontElements(surface);
        };
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

        const allowedTokens = Array.isArray(options.allowedTokens) && options.allowedTokens.length > 0
            ? new Set(options.allowedTokens)
            : null;

        const isTokenAllowed = (token?: string) => !allowedTokens || !token || allowedTokens.has(token);

        const registerButton = (button: HTMLButtonElement, config: ToolbarButtonConfig) => {
            button.dataset.command = config.command;
            if (config.value) {
                button.dataset.value = config.value;
            }
            if (config.toggles) {
                button.dataset.toggles = "true";
            }
            trackedButtons.push({ element: button, config });
        };

        const registerSelect = (select: HTMLSelectElement, config: ToolbarSelectConfig) => {
            select.dataset.command = config.command;
            trackedSelects.push({ element: select, config });
        };

        const createButton = (config: ToolbarButtonConfig) => {
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

        const createSelectControl = (config: ToolbarSelectConfig) => {
            const control = document.createElement("label");
            control.className = "bugence-editor-toolbar__control";

            const badge = document.createElement("span");
            badge.className = "bugence-editor-toolbar__control-badge";
            badge.textContent = config.label;
            control.appendChild(badge);

            const select = document.createElement("select");
            select.className = "bugence-editor-toolbar__select";

            config.options.forEach((option) => {
                const optionNode = document.createElement("option");
                optionNode.value = option.value;
                optionNode.textContent = option.label;
                select.appendChild(optionNode);
            });

            select.addEventListener("change", (event) => {
                event.preventDefault();
                const nextValue = select.value;
                if (config.command === "fontName") {
                    if (!nextValue) {
                        clearInlineStyle("font-family");
                    } else {
                        execCommand(surface, "fontName", nextValue);
                    }
                } else if (config.command === "fontSize") {
                    if (!nextValue) {
                        clearInlineStyle("font-size");
                    } else {
                        execCommand(surface, "fontSize", nextValue);
                    }
                } else {
                    execCommand(surface, config.command, nextValue);
                }
                updateStates();
            });

            registerSelect(select, config);

            control.appendChild(select);
            return { control, select };
        };

        const appendDivider = () => {
            const divider = document.createElement("span");
            divider.className = "bugence-editor-toolbar__divider";
            toolbar.appendChild(divider);
        };

        const createSwatch = (group: ToolbarPaletteConfig, config: { label: string; value: string; preview?: string }) => {
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
                swatch.dataset.swatchType = "reset";
            } else if (config.value === "inherit") {
                swatch.style.background = "linear-gradient(135deg, rgba(148, 163, 184, 0.24), rgba(15, 23, 42, 0.85))";
                swatch.dataset.swatchType = "reset";
            }

            swatch.addEventListener("click", (event) => {
                event.preventDefault();
                if (group.command === "foreColor" && config.value === "inherit") {
                    clearInlineStyle("color");
                } else if (group.command === "hiliteColor" && config.value === "transparent") {
                    clearInlineStyle("background-color");
                } else {
                    execCommand(surface, group.command, config.value);
                }
                Array.from(swatch.parentElement!.querySelectorAll(".bugence-editor-toolbar__swatch")).forEach((node) => {
                    (node as HTMLElement).dataset.selected = node === swatch ? "true" : "false";
                });
                updateStates();
            });

            return swatch;
        };

        const createCustomColorControl = (palette: ToolbarPaletteConfig) => {
            const control = document.createElement("label");
            control.className = "bugence-editor-toolbar__control bugence-editor-toolbar__control--inline";

            const badge = document.createElement("span");
            badge.className = "bugence-editor-toolbar__control-badge";
            badge.textContent = "Custom";
            control.appendChild(badge);

            const input = document.createElement("input");
            input.type = "color";
            input.className = "bugence-editor-toolbar__color-input";
            input.value = palette.command === "hiliteColor" ? "#fde68a" : "#ffffff";

            input.addEventListener("input", () => {
                execCommand(surface, palette.command, input.value);
                Array.from(control.parentElement?.querySelectorAll(".bugence-editor-toolbar__swatch") ?? []).forEach((node) => {
                    (node as HTMLElement).dataset.selected = "false";
                });
                updateStates();
            });

            control.appendChild(input);
            return control;
        };

        const groups: Array<{ label?: string; buttons: ToolbarButtonConfig[] }> = [
            {
                label: "History",
                buttons: [
                    { label: "Undo", command: "undo", title: "Undo (Ctrl/Cmd + Z)", token: "undo" },
                    { label: "Redo", command: "redo", title: "Redo (Ctrl/Cmd + Y)", token: "redo" }
                ]
            },
            {
                label: "Type",
                buttons: [
                    { label: "Bold", command: "bold", toggles: true, token: "bold" },
                    { label: "Italic", command: "italic", toggles: true, token: "italic" },
                    { label: "Underline", command: "underline", toggles: true, token: "underline" },
                    { label: "Strike", command: "strikeThrough", toggles: true, token: "strike" },
                    { label: "Superscript", command: "superscript", toggles: true, token: "superscript" },
                    { label: "Subscript", command: "subscript", toggles: true, token: "subscript" }
                ]
            },
            {
                label: "Structure",
                buttons: [
                    { label: "Heading", command: "formatBlock", value: "h2", title: "Apply H2", token: "h2" },
                    { label: "Subheading", command: "formatBlock", value: "h3", title: "Apply H3", token: "h3" },
                    { label: "Paragraph", command: "formatBlock", value: "p", title: "Reset to paragraph", token: "paragraph" },
                    { label: "Quote", command: "formatBlock", value: "blockquote", title: "Block quote", token: "blockquote" },
                    { label: "Rule", command: "insertHorizontalRule", title: "Insert horizontal divider", token: "horizontalRule" }
                ]
            },
            {
                label: "Lists",
                buttons: [
                    { label: "Bullet", command: "insertUnorderedList", toggles: true, token: "unorderedList" },
                    { label: "Numbered", command: "insertOrderedList", toggles: true, token: "orderedList" }
                ]
            },
            {
                label: "Alignment",
                buttons: [
                    { label: "Left", command: "justifyLeft", token: "alignLeft" },
                    { label: "Center", command: "justifyCenter", token: "alignCenter" },
                    { label: "Right", command: "justifyRight", token: "alignRight" },
                    { label: "Justify", command: "justifyFull", token: "justify" }
                ]
            },
            {
                label: "Links",
                buttons: [
                    { label: "Link", command: "createLink", token: "link" },
                    { label: "Unlink", command: "unlink", token: "unlink" },
                    { label: "Clear", command: "removeFormat", token: "removeFormat" }
                ]
            }
        ];

        const selects: ToolbarSelectConfig[] = [
            {
                label: "Font family",
                command: "fontName",
                token: "fontFamily",
                options: FONT_FAMILY_OPTIONS
            },
            {
                label: "Font size",
                command: "fontSize",
                token: "fontSize",
                options: [{ label: "Auto (Theme)", value: "" }, ...FONT_SIZE_OPTIONS]
            }
        ];

        const palettes: ToolbarPaletteConfig[] = [
            {
                label: "Text",
                command: "foreColor",
                token: "color",
                allowCustom: true,
                swatches: [
                    { label: "Reset", value: "inherit", preview: "linear-gradient(135deg, rgba(148,163,184,0.24), rgba(15,23,42,0.85))" },
                    { label: "Night", value: "#0f172a" },
                    { label: "Charcoal", value: "#1f2937" },
                    { label: "Slate", value: "#334155" },
                    { label: "Pearl", value: "#e2e8f0" },
                    { label: "Soft White", value: "#f8fafc" },
                    { label: "Sky", value: "#38bdf8" },
                    { label: "Ocean", value: "#0ea5e9" },
                    { label: "Indigo", value: "#6366f1" },
                    { label: "Violet", value: "#7c3aed" },
                    { label: "Lavender", value: "#a855f7" },
                    { label: "Emerald", value: "#34d399" },
                    { label: "Teal", value: "#14b8a6" },
                    { label: "Lime", value: "#84cc16" },
                    { label: "Amber", value: "#facc15" },
                    { label: "Sunset", value: "#f97316" },
                    { label: "Coral", value: "#fb7185" },
                    { label: "Rose", value: "#e11d48" },
                    { label: "Crimson", value: "#ef4444" },
                    { label: "Coffee", value: "#78350f" }
                ]
            },
            {
                label: "Highlight",
                command: "hiliteColor",
                token: "highlight",
                allowCustom: true,
                swatches: [
                    { label: "None", value: "transparent", preview: "linear-gradient(135deg, rgba(148,163,184,0.35), rgba(15,23,42,0.25))" },
                    { label: "Glow", value: "#fde68a" },
                    { label: "Lemon", value: "#fef08a" },
                    { label: "Candle", value: "#fcd34d" },
                    { label: "Peach", value: "#fed7aa" },
                    { label: "Rose", value: "#fbcfe8" },
                    { label: "Blush", value: "#f9a8d4" },
                    { label: "Ice", value: "#bae6fd" },
                    { label: "Mist", value: "#cbd5f5" },
                    { label: "Lilac", value: "#e9d5ff" },
                    { label: "Mint", value: "#bbf7d0" },
                    { label: "Aqua", value: "#99f6e4" }
                ]
            }
        ];

        const activeGroups = groups
            .map((group) => ({
                label: group.label,
                buttons: group.buttons.filter((button) => isTokenAllowed(button.token ?? button.command))
            }))
            .filter((group) => group.buttons.length > 0);

        const activeSelects = selects.filter((select) => isTokenAllowed(select.token));
        const activePalettes = palettes.filter((palette) => isTokenAllowed(palette.token));

        activeGroups.forEach((group, groupIndex) => {
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

            if (groupIndex < activeGroups.length - 1 || activeSelects.length > 0 || activePalettes.length > 0) {
                appendDivider();
            }
        });

        if (activeSelects.length > 0) {
            const container = document.createElement("div");
            container.className = "bugence-editor-toolbar__group";

            const label = document.createElement("span");
            label.className = "bugence-editor-toolbar__label";
            label.textContent = "Typography";
            container.appendChild(label);

            const actions = document.createElement("div");
            actions.className = "bugence-editor-toolbar__actions";

            activeSelects.forEach((selectConfig) => {
                const { control } = createSelectControl(selectConfig);
                actions.appendChild(control);
            });

            container.appendChild(actions);
            toolbar.appendChild(container);

            if (activePalettes.length > 0) {
                appendDivider();
            }
        }

        activePalettes.forEach((palette, paletteIndex) => {
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

            if (palette.allowCustom) {
                container.appendChild(createCustomColorControl(palette));
            }

            toolbar.appendChild(container);

            if (paletteIndex < activePalettes.length - 1) {
                appendDivider();
            }
        });

        const updateStates = () => {
            let blockContext: string | null = null;
            try {
                blockContext = document.queryCommandValue("formatBlock") as string | null;
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

            trackedSelects.forEach(({ element, config }) => {
                let candidateValue = "";

                if (config.command === "fontName") {
                    let raw = "";
                    try {
                        raw = document.queryCommandValue("fontName") as string;
                    } catch (error) {
                        raw = "";
                    }

                    const resolvedKey = resolveFontFamilyKey(raw);
                    if (resolvedKey && FONT_FAMILY_MAP.has(resolvedKey)) {
                        candidateValue = resolvedKey;
                    } else if (resolvedKey) {
                        candidateValue = resolvedKey;
                    } else {
                        candidateValue = "";
                    }
                } else if (config.command === "fontSize") {
                    let raw = "";
                    try {
                        raw = document.queryCommandValue("fontSize") as string;
                    } catch (error) {
                        raw = "";
                    }

                    const normalized = normalizeFontSizeValue(raw);
                    if (normalized && FONT_SIZE_VALUE_TO_COMMAND.has(normalized)) {
                        candidateValue = FONT_SIZE_VALUE_TO_COMMAND.get(normalized) ?? "";
                    } else if (raw && FONT_SIZE_MAP.has(raw)) {
                        candidateValue = raw;
                    } else {
                        candidateValue = "";
                    }
                }

                const optionValues = Array.from(element.options).map((option) => option.value);
                element.value = candidateValue && optionValues.includes(candidateValue) ? candidateValue : "";
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

        (toolbar as any).updateStates = updateStates;
        (toolbar as any).handleSelectionChange = handleSelectionChange;

        return toolbar as typeof toolbar & { updateStates: () => void; handleSelectionChange: () => void };
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

        let currentSection = section ? normalizeSection(section) : section;
        const anchorTarget = element?.closest?.("a") ?? (element?.tagName === "A" ? (element as HTMLElement) : null);
        const baselineHref = anchorTarget ? anchorTarget.getAttribute("href") ?? "" : "";
        const baselineTargetBlank = anchorTarget ? anchorTarget.getAttribute("target") === "_blank" : false;
        let sectionMode = detectSectionMode(currentSection);

        const descriptor = resolveDescriptorForContext(currentSection, element);
        if (descriptor?.schema?.contentType) {
            sectionMode = descriptor.schema.contentType;
        }

        let fieldRender = renderSectionFields({
            descriptor,
            section: currentSection,
            element
        });

        if (fieldRender.kind !== "text") {
            fieldRender = renderSectionFields({ descriptor: null, section: currentSection, element }) as TextFieldRenderResult;
        }

        const textFieldRender = fieldRender as TextFieldRenderResult;
        const contentFieldSchema: SectionFieldSchema | undefined = textFieldRender.fieldSchema;

        const header = document.createElement("header");
        header.className = "bugence-editor-modal__header";
        const modeLabel = sectionMode === "Text" ? "Plain text" : sectionMode === "Html" ? "Raw HTML" : "Rich text";
        const modalTitle = descriptor?.schema?.title ?? currentSection?.title ?? "Edit content";
        const headerTitle = escapeHtml(modalTitle);
        const headerSubtitle = escapeHtml([config.pageName ?? "Page", modeLabel].filter(Boolean).join(" - "));
        header.innerHTML = `
            <div>
                <div class="bugence-editor-modal__title">${headerTitle}</div>
                <p class="bugence-editor-modal__subtitle">${headerSubtitle}</p>
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

        const surface = textFieldRender.surface;
        if (textFieldRender.helperElement) {
            body.appendChild(textFieldRender.helperElement);
        }

        const linkPanel = document.createElement("div");
        linkPanel.className = "bugence-editor-link-panel";
        const linkLabel = document.createElement("label");
        linkLabel.className = "bugence-editor-link-panel__field";
        linkLabel.innerHTML = `<span>Link target</span>`;
        const linkInput = document.createElement("input");
        linkInput.type = "url";
        linkInput.placeholder = "https://example.com";
        linkInput.value = baselineHref;
        linkInput.className = "bugence-editor-input";
        linkLabel.appendChild(linkInput);

        const targetToggle = document.createElement("label");
        targetToggle.className = "bugence-editor-link-panel__toggle";
        const targetCheckbox = document.createElement("input");
        targetCheckbox.type = "checkbox";
        targetCheckbox.checked = baselineTargetBlank;
        const toggleText = document.createElement("span");
        toggleText.textContent = "Open in new tab";
        targetToggle.append(targetCheckbox, toggleText);

        linkPanel.append(linkLabel, targetToggle);
        body.appendChild(linkPanel);

        const toolbar = buildToolbar(surface, {
            allowedTokens: textFieldRender.toolbarTokens
        });
        body.appendChild(toolbar);
        body.appendChild(surface);

        const resolveInitialValue = () => {
            if (typeof currentSection?.contentValue === "string" && currentSection.contentValue.length > 0) {
                return currentSection.contentValue;
            }

            if (typeof descriptor?.schema?.defaults?.contentValue === "string") {
                return descriptor.schema.defaults.contentValue;
            }

            if (sectionMode === "Text") {
                return element?.textContent ?? "";
            }

            return element?.innerHTML ?? "";
        };

        const createSnapshotFromValue = (rawValue: string, syncSurface = false) => {
            const sanitized = textFieldRender.sanitize(rawValue ?? "");

            if (syncSurface && sectionMode === "Text" && sanitized.plain !== rawValue) {
                surface.textContent = sanitized.plain;
            } else if (syncSurface && sectionMode !== "Text" && sanitized.html !== rawValue) {
                surface.innerHTML = sanitized.html;
            }

            const htmlValue = sectionMode === "Text"
                ? plainTextToHtml(sanitized.plain)
                : sanitized.html;

            return {
                mode: sectionMode,
                plain: sanitized.plain,
                html: htmlValue,
                storageValue: sectionMode === "Text" ? sanitized.storageValue : sanitized.html,
                wasTruncated: sanitized.wasTruncated
            };
        };

        let savedSnapshot = createSnapshotFromValue(resolveInitialValue(), false);
        let previousSnapshot =
            typeof currentSection?.previousContentValue === "string" && currentSection.previousContentValue.length > 0
                ? createSnapshotFromValue(currentSection.previousContentValue, false)
                : null;

        const hasPreviousSnapshot = () => previousSnapshot !== null && !snapshotsEqual(previousSnapshot, savedSnapshot);

        const applySnapshotToSurface = (snapshot) => {
            if (snapshot.mode === "Text") {
                surface.textContent = snapshot.plain ?? "";
                return;
            }

            surface.innerHTML = snapshot.html ?? "";
        };

        const applySnapshotToElement = (snapshot) => {
            if (!element) {
                return;
            }

            const nextHtml = snapshot.html ?? "";
            if (normalizeHtml(element.innerHTML) !== normalizeHtml(nextHtml)) {
                element.innerHTML = nextHtml;
            }
        };

        const captureSnapshot = () => {
            const rawValue = sectionMode === "Text"
                ? surface.innerText ?? surface.textContent ?? ""
                : surface.innerHTML ?? "";

            return createSnapshotFromValue(rawValue, true);
        };

        applySnapshotToSurface(savedSnapshot);
        applySnapshotToElement(savedSnapshot);

        const footer = document.createElement("footer");
        footer.className = "bugence-editor-modal__footer";
        footer.innerHTML = `
            <span class="bugence-editor-notice">Updates save to mission control. Publish to push live.</span>
        `;

        const actions = document.createElement("div");
        actions.className = "bugence-editor-actions";

        const updateToolbarStates = () => {
            if (typeof toolbar.updateStates === "function") {
                toolbar.updateStates();
            }
        };

        surface.addEventListener("keyup", updateToolbarStates);
        surface.addEventListener("mouseup", updateToolbarStates);

        const selectionHandler = () => {
            const selection = document.getSelection();
            if (!selection) {
                return;
            }

            const anchorNode = selection.anchorNode;
            if (!anchorNode) {
                return;
            }

            if (surface.contains(anchorNode)) {
                if (typeof toolbar.handleSelectionChange === "function") {
                    toolbar.handleSelectionChange();
                } else {
                    updateToolbarStates();
                }
            }
        };

        document.addEventListener("selectionchange", selectionHandler);
        state.selectionHandler = selectionHandler;

        updateToolbarStates();

        const validateFile = (file: File | null | undefined) => {
            if (!file) {
                return true;
            }

            if (imageRender.accept?.length) {
                const lowerType = (file.type || "").toLowerCase();
                const lowerName = file.name.toLowerCase();

                const matchesAccept = imageRender.accept.some((pattern) => {
                    const lower = pattern.toLowerCase();
                    if (lower === "image/*") {
                        return lowerType.startsWith("image/");
                    }
                    if (lower.endsWith("/*")) {
                        const base = lower.split("/")[0];
                        return lowerType.startsWith(`${base}/`);
                    }
                    if (lower.startsWith(".")) {
                        return lowerName.endsWith(lower);
                    }
                    return lowerType === lower;
                });

                if (!matchesAccept) {
                    showToast(`Unsupported file type. Allowed: ${imageRender.accept.join(", ")}`, "error");
                    return false;
                }
            }

            if (typeof imageRender.maxFileSizeMB === "number" && imageRender.maxFileSizeMB > 0) {
                const maxBytes = imageRender.maxFileSizeMB * 1_048_576;
                if (file.size > maxBytes) {
                    showToast(`Image must be ${imageRender.maxFileSizeMB}MB or smaller.`, "error");
                    return false;
                }
            }

            return true;
        };

        const normalizeLinkHref = (value: string) => {
            const trimmed = (value || "").trim();
            if (!trimmed) {
                return "";
            }

            if (/^(?:https?:|mailto:|tel:|sms:|ftp:|#|\/|\?)/i.test(trimmed)) {
                return trimmed;
            }

            return `https://${trimmed.replace(/^\/+/, "")}`;
        };

        const applyLinkToHtml = (html: string, plain: string) => {
            if (!anchorTarget && !linkInput.value.trim()) {
                return html;
            }

            const wrapper = document.createElement("div");
            wrapper.innerHTML = html || "";

            let anchor = wrapper.querySelector("a");
            if (!anchor) {
                anchor = document.createElement("a");
                anchor.innerHTML = wrapper.innerHTML?.trim().length ? wrapper.innerHTML : escapeHtml(plain ?? "");
                wrapper.innerHTML = "";
                wrapper.appendChild(anchor);
            }

            const hrefVal = normalizeLinkHref(linkInput.value || "");
            if (hrefVal.length > 0) {
                anchor.setAttribute("href", hrefVal);
            } else {
                anchor.removeAttribute("href");
            }

            if (targetCheckbox.checked) {
                anchor.setAttribute("target", "_blank");
            } else {
                anchor.removeAttribute("target");
            }

            return wrapper.innerHTML;
        };

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

        const syncDirtyState = () => {
            const draft = captureSnapshot();
            const isDirty = !snapshotsEqual(draft, savedSnapshot);

            revert.disabled = !isDirty && !hasPreviousSnapshot();
            save.disabled = !isDirty;
        };

        save.addEventListener("click", async () => {
            const draft = captureSnapshot();

            if (draft.wasTruncated) {
                const maxLabel = typeof contentFieldSchema?.maxLength === "number"
                    ? `${contentFieldSchema.maxLength} characters`
                    : "the allowed length";
                showToast(`Content exceeds ${maxLabel}. Shorten the text before saving.`, "error");
                surface.focus({ preventScroll: true });
                return;
            }

            if (contentFieldSchema?.required) {
                const candidatePlain = (draft.plain ?? "").trim();
                if (candidatePlain.length === 0) {
                    showToast(`${contentFieldSchema.label ?? "Content"} is required.`, "error");
                    surface.focus({ preventScroll: true });
                    return;
                }
            }

            const form = new FormData();
            if (currentSection?.id) {
                form.append("SectionId", currentSection.id);
            }

            const selector = currentSection?.cssSelector ?? computeSelector(element);
            if (selector) {
                form.append("Selector", selector);
            }

            let contentToSave = draft.storageValue;
            if (anchorTarget || linkInput.value.trim().length > 0) {
                const baseHtml = sectionMode === "Text" ? plainTextToHtml(draft.plain) : draft.storageValue;
                contentToSave = applyLinkToHtml(baseHtml, draft.plain);

                if (anchorTarget) {
                    const hrefVal = normalizeLinkHref(linkInput.value || "");
                    if (hrefVal.length > 0) {
                        anchorTarget.setAttribute("href", hrefVal);
                    } else {
                        anchorTarget.removeAttribute("href");
                    }
                    if (targetCheckbox.checked) {
                        anchorTarget.setAttribute("target", "_blank");
                    } else {
                        anchorTarget.removeAttribute("target");
                    }
                }
            }

            form.append("ContentType", draft.mode);
            form.append("ContentValue", contentToSave);

            try {
                save.disabled = true;
                revert.disabled = true;
                cancel.disabled = true;
                const result = await saveSection(element, form);
                if (result?.section) {
                    normalizeSection(result.section);
                    currentSection = result.section;
                    sectionMode = detectSectionMode(currentSection);

                    const nextRawValue = typeof currentSection.contentValue === "string"
                        ? currentSection.contentValue
                        : sectionMode === "Text"
                            ? draft.plain
                            : contentToSave;

                    savedSnapshot = createSnapshotFromValue(nextRawValue ?? "", false);
                    previousSnapshot =
                        typeof currentSection.previousContentValue === "string" && currentSection.previousContentValue.length > 0
                            ? createSnapshotFromValue(currentSection.previousContentValue, false)
                            : null;

                    applySnapshotToElement(savedSnapshot);
                } else {
                    savedSnapshot = draft;
                    applySnapshotToElement(savedSnapshot);
                }

                closeOverlay();
                showToast("Content updated.");
            } catch (error) {
                console.error(error);
                showToast(error.message ?? "Unable to save content.", "error");
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
            const draft = captureSnapshot();

            if (!snapshotsEqual(draft, savedSnapshot)) {
                applySnapshotToSurface(savedSnapshot);
                applySnapshotToElement(savedSnapshot);
            } else if (hasPreviousSnapshot() && previousSnapshot) {
                applySnapshotToSurface(previousSnapshot);
                applySnapshotToElement(previousSnapshot);
            }

            updateToolbarStates();
            syncDirtyState();
        });

        surface.addEventListener("input", () => {
            const draft = captureSnapshot();
            applySnapshotToElement(draft);
            syncDirtyState();
        });

        syncDirtyState();

        actions.appendChild(revert);
        actions.appendChild(cancel);
        actions.appendChild(save);
        footer.appendChild(actions);

        modal.appendChild(header);
        modal.appendChild(body);
        modal.appendChild(footer);

        overlay.appendChild(modal);
        document.body.appendChild(overlay);
        surface.focus({ preventScroll: true });
    };
    const openImageEditor = (element, section) => {
        const overlay = ensureOverlay();
        overlay.innerHTML = "";

        const modal = document.createElement("article");
        modal.className = "bugence-editor-modal";
        modal.dataset.bugenceIgnore = "true";

        const descriptor = resolveDescriptorForContext(section, element);
        let fieldRender = renderSectionFields({
            descriptor,
            section,
            element
        });

        if (fieldRender.kind !== "image") {
            fieldRender = renderSectionFields({ descriptor: null, section, element }) as ImageFieldRenderResult;
        }

        const imageRender = fieldRender as ImageFieldRenderResult;
        const imageFieldSchema: SectionFieldSchema | undefined = imageRender.imageField;
        const altFieldSchema: SectionFieldSchema | undefined = imageRender.altField;

        const header = document.createElement("header");
        header.className = "bugence-editor-modal__header";
        const imageModalTitle = descriptor?.schema?.title ?? section?.title ?? "Update image";
        const imageSubtitle = `${config.pageName ?? "Page"} - Image`;
        header.innerHTML = `
            <div>
                <div class="bugence-editor-modal__title">${escapeHtml(imageModalTitle)}</div>
                <p class="bugence-editor-modal__subtitle">${escapeHtml(imageSubtitle)}</p>
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
        body.appendChild(imageRender.container);

        const footer = document.createElement("footer");
        footer.className = "bugence-editor-modal__footer";
        footer.innerHTML = `
            <span class="bugence-editor-notice">Updates save to mission control. Publish to push live.</span>
        `;

        const actions = document.createElement("div");
        actions.className = "bugence-editor-actions";

        const fileInput = imageRender.fileInput;
        const altInput = imageRender.altInput;
        const preview = imageRender.preview;

        let currentBaselineSrc = preview.getAttribute("src") ?? "";
        if (!currentBaselineSrc && element instanceof HTMLImageElement && element.src) {
            currentBaselineSrc = element.src;
        }
        if (!currentBaselineSrc && typeof descriptor?.schema?.defaults?.contentValue === "string") {
            currentBaselineSrc = descriptor.schema.defaults.contentValue;
        }
        if (currentBaselineSrc) {
            preview.src = currentBaselineSrc;
        }

        const initialAltSource =
            (typeof section?.mediaAltText === "string" ? section.mediaAltText : undefined)
            ?? (element instanceof HTMLImageElement ? element.alt : undefined)
            ?? descriptor?.schema?.defaults?.mediaAltText
            ?? altFieldSchema?.placeholder
            ?? "";

        let currentBaselineAlt = imageRender.sanitizeAlt(initialAltSource);
        altInput.value = currentBaselineAlt;

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

        const syncDirtyState = () => {
            const hasFile = Boolean(fileInput.files && fileInput.files.length);
            const altDirty = imageRender.sanitizeAlt(altInput.value) !== currentBaselineAlt;
            const imageDirty = preview.src !== currentBaselineSrc;
            const isDirty = hasFile || altDirty || imageDirty;

            revert.disabled = !isDirty;
            save.disabled = !isDirty;
        };

        fileInput.addEventListener("change", () => {
            const [file] = fileInput.files ?? [];
            if (!file) {
                preview.src = currentBaselineSrc;
                syncDirtyState();
                return;
            }

            if (!validateFile(file)) {
                fileInput.value = "";
                preview.src = currentBaselineSrc;
                syncDirtyState();
                return;
            }

            const reader = new FileReader();
            reader.onload = (event) => {
                const nextSrc = typeof event.target?.result === "string" ? event.target.result : "";
                if (nextSrc) {
                    preview.src = nextSrc;
                }
                syncDirtyState();
            };
            reader.readAsDataURL(file);
        });

        altInput.addEventListener("input", syncDirtyState);

        save.addEventListener("click", async () => {
            const selectedFile = fileInput.files && fileInput.files[0];
            const altValue = imageRender.sanitizeAlt(altInput.value);

            if (selectedFile && !validateFile(selectedFile)) {
                fileInput.value = "";
                preview.src = currentBaselineSrc;
                syncDirtyState();
                return;
            }

            if (imageFieldSchema?.required && !selectedFile && !currentBaselineSrc) {
                showToast(`${imageFieldSchema.label ?? "Image"} is required.`, "error");
                return;
            }

            if (altFieldSchema?.required && altValue.length === 0) {
                showToast(`${altFieldSchema.label ?? "Alt text"} is required.`, "error");
                altInput.focus();
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
                    currentBaselineSrc = result.section.mediaPath;
                } else if (selectedFile) {
                    currentBaselineSrc = preview.src;
                }

                currentBaselineAlt = altValue;
                if (element instanceof HTMLImageElement) {
                    element.src = currentBaselineSrc;
                    element.alt = currentBaselineAlt;
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
            preview.src = currentBaselineSrc;
            if (element instanceof HTMLImageElement) {
                element.src = currentBaselineSrc;
            }

            altInput.value = currentBaselineAlt;
            fileInput.value = "";
            syncDirtyState();
        });

        syncDirtyState();

        const actionsFragment = document.createDocumentFragment();
        actionsFragment.appendChild(revert);
        actionsFragment.appendChild(cancel);
        actionsFragment.appendChild(save);
        actions.appendChild(actionsFragment);
        footer.appendChild(actions);

        modal.appendChild(header);
        modal.appendChild(body);
        modal.appendChild(footer);

        overlay.appendChild(modal);
        document.body.appendChild(overlay);
    };

    const openEditor = (element: Element | null | undefined) => {
        if (!element) {
            return;
        }

        if (state.overlay && element instanceof Node && state.overlay.contains(element)) {
            return;
        }

        const context = getSectionContext(element);
        const section = context?.section ?? null;
        if (!section) {
            console.warn("[bugence Editor] Unable to resolve section metadata for element.", element);
            showToast("Unable to determine which section this block belongs to.", "error");
            return;
        }

        state.activeSection = section;

        let hostCandidate: Element | null = context?.element ?? (element instanceof Element ? element : null);
        const resolvedHost = findElementForSection(section);
        if (resolvedHost) {
            hostCandidate = resolvedHost;
        }

        if (hostCandidate && shouldLockElement(hostCandidate)) {
            section.isLocked = true;
            if (hostCandidate instanceof HTMLElement) {
                hostCandidate.setAttribute("data-bugence-locked", "true");
            }
        }

        if (isLocked(section)) {
            showToast("This block is locked by the Bugence team.", "error");
            return;
        }

        const hostElement = hostCandidate as HTMLElement | null;
        if (!hostElement) {
            showToast("Unable to load editor for this block.", "error");
            return;
        }

        const sectionType = (section.contentType ?? section.ContentType ?? "").toString().toLowerCase();
        if (isImageElement(hostElement) || sectionType === "image") {
            openImageEditor(hostElement, section);
            return;
        }

        openTextEditor(hostElement, section);
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
        document.body?.classList.add("bugence-editor-active");
        document.documentElement?.classList.add("bugence-editor-active");

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
            console.error("[bugence Editor] Failed to initialize", error);
            state.page = null;
            // Keep the editor chrome visible so the user can see the failure reason instead of a silent disappearance.
            showToast(
                error instanceof Error && error.message ? error.message : "Visual editor failed to load.",
                "error"
            );
            const fallbackSidebar = ensureSidebar();
            fallbackSidebar.publishButton.disabled = true;
            fallbackSidebar.statusLabel.textContent = "Editor unavailable";
            fallbackSidebar.statusMeta.textContent =
                (error instanceof Error && error.message) || "Unable to load editable sections.";
        }
    };

    if (document.readyState === "complete" || document.readyState === "interactive") {
        window.setTimeout(bootstrap, 0);
    } else {
        document.addEventListener("DOMContentLoaded", bootstrap);
    }
})();

export {};






