(() => {
    const extractPageIdFromPath = () => {
        const match = window.location.pathname.match(/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})/i);
        return match ? match[1] : null;
    };

    let config = window.__bugenceEditor;
    if (!config || !config.pageId) {
        const fallbackId = extractPageIdFromPath();
        if (fallbackId) {
            config = window.__bugenceEditor = { pageId: fallbackId, apiBase: "/api/content" };
        }
    }

    if (!config || !config.pageId) {
        return;
    }

    const API_BASE = config.apiBase ?? "/api/content";
    const PAGE_ENDPOINT = `${API_BASE}/pages/${config.pageId}`;
    const DELETE_ENDPOINT = (sectionId) => `${API_BASE}/pages/${config.pageId}/sections/${sectionId}`;

    const state = {
        sectionsByKey: new Map(),
        sectionsBySelector: new Map(),
        currentKey: null,
        currentSelector: null
    };

    const sanitizeHtml = (value) => {
        if (!value) {
            return "";
        }

        return value
            .replace(/>\s+</g, "><")
            .replace(/\s{2,}/g, " ")
            .trim();
    };

    const computeSelector = (element) => {
        if (!element || !(element instanceof Element)) {
            return "";
        }

        const segments = [];
        let current = element;

        while (current && current.nodeType === Node.ELEMENT_NODE && current !== document.body) {
            let segment = current.tagName.toLowerCase();
            if (current.id) {
                segment += `#${CSS.escape(current.id)}`;
                segments.unshift(segment);
                break;
            }

            const classes = Array.from(current.classList ?? []).filter(Boolean);
            if (classes.length) {
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

        return segments.join(" > ");
    };

    const getToastStack = () => {
        let stack = document.querySelector(".bugence-editor-toast-stack");
        if (!stack) {
            stack = document.createElement("div");
            stack.className = "bugence-editor-toast-stack";
            document.body.appendChild(stack);
        }
        return stack;
    };

    const showToast = (message, tone = "info") => {
        const stack = getToastStack();
        const toast = document.createElement("div");
        toast.className = "bugence-editor-toast";
        if (tone === "error") {
            toast.classList.add("bugence-editor-toast--error");
        } else if (tone === "success") {
            toast.classList.add("bugence-editor-toast--success");
        }
        toast.textContent = message;
        stack.appendChild(toast);

        window.setTimeout(() => {
            toast.classList.add("bugence-editor-toast--leaving");
            window.setTimeout(() => toast.remove(), 260);
        }, 2400);
    };

    const registerSections = (sections) => {
        state.sectionsByKey.clear();
        state.sectionsBySelector.clear();

        sections.forEach((section) => {
            if (!section) {
                return;
            }

            const key = section.sectionKey ?? section.SectionKey ?? null;
            const selector = section.cssSelector ?? section.CssSelector ?? null;
            if (key) {
                state.sectionsByKey.set(key, section);
            }
            if (selector) {
                state.sectionsBySelector.set(selector, section);
            }
        });
    };

    const fetchSections = async () => {
        try {
            const response = await fetch(PAGE_ENDPOINT, { credentials: "include" });
            const payload = await response.json();
            const sections = payload?.sections ?? payload?.Sections ?? null;
            if (Array.isArray(sections)) {
                registerSections(sections);
            }
        } catch (error) {
            console.warn("[Bugence Editor+]", "Unable to refresh section metadata", error);
        }
    };

    const resolveSectionMetadata = async () => {
        const key = state.currentKey ?? null;
        const selector = state.currentSelector ?? null;

        if (key && state.sectionsByKey.has(key)) {
            return state.sectionsByKey.get(key);
        }

        if (selector && state.sectionsBySelector.has(selector)) {
            return state.sectionsBySelector.get(selector);
        }

        await fetchSections();

        if (key && state.sectionsByKey.has(key)) {
            return state.sectionsByKey.get(key);
        }

        if (selector && state.sectionsBySelector.has(selector)) {
            return state.sectionsBySelector.get(selector);
        }

        return null;
    };

    const updateMetrics = (container, surface) => {
        const wordsNode = container.querySelector("[data-bugence-enhanced-words]");
        const charsNode = container.querySelector("[data-bugence-enhanced-chars]");
        if (!wordsNode || !charsNode) {
            return;
        }

        const text = surface.textContent ?? "";
        const words = text.replace(/\s+/g, " ").trim().split(" ").filter(Boolean);

        wordsNode.textContent = words.length ? String(words.length) : "0";
        charsNode.textContent = String(text.trim().length);
    };

    const deleteSection = async (sectionId) => {
        if (!sectionId) {
            throw new Error("Section id is unavailable.");
        }

        const response = await fetch(DELETE_ENDPOINT(sectionId), {
            method: "DELETE",
            credentials: "include"
        });

        if (!response.ok) {
            const payload = await response.json().catch(() => null);
            throw new Error(payload?.message ?? `Unable to delete section (HTTP ${response.status}).`);
        }

        await fetchSections();
    };

    const enhanceModal = (modal) => {
        if (!modal || modal.dataset.bugenceEnhanced === "true") {
            return;
        }

        const surface = modal.querySelector(".bugence-editor-surface");
        if (!surface) {
            return;
        }

        modal.dataset.bugenceEnhanced = "true";

        const header = modal.querySelector(".bugence-editor-modal__header");
        if (header) {
            header.classList.add("bugence-editor-modal__header--revamp");

            const title = header.querySelector(".bugence-editor-modal__title");
            const subtitle = header.querySelector(".bugence-editor-modal__subtitle");
            const existingHeading = header.querySelector(".bugence-editor-modal__heading");

            if (title && subtitle && !existingHeading) {
                const eyebrow = document.createElement("span");
                eyebrow.className = "bugence-editor-modal__eyebrow";
                eyebrow.textContent = "Rich text block";

                title.classList.add("bugence-editor-modal__title--revamp");
                subtitle.classList.add("bugence-editor-modal__subtitle--revamp");

                const heading = document.createElement("div");
                heading.className = "bugence-editor-modal__heading";
                heading.append(eyebrow, title, subtitle);
                header.insertBefore(heading, header.firstChild);
            }

            const closeButton = header.querySelector(".bugence-editor-modal__close");
            if (closeButton && !closeButton.classList.contains("bugence-editor-modal__icon")) {
                closeButton.classList.add("bugence-editor-modal__icon");
                closeButton.innerHTML = `<span class="fa-solid fa-xmark" aria-hidden="true"></span>`;
                let actions = header.querySelector(".bugence-editor-modal__header-actions");
                if (!actions) {
                    actions = document.createElement("div");
                    actions.className = "bugence-editor-modal__header-actions";
                    header.appendChild(actions);
                }
                actions.appendChild(closeButton);
            }

            const applySubtitle = (sectionKey) => {
                const subtitleNode = header.querySelector(".bugence-editor-modal__subtitle--revamp") ?? header.querySelector(".bugence-editor-modal__subtitle");
                if (!subtitleNode) {
                    return;
                }
                const suffix = sectionKey ?? state.currentKey ?? "Section";
                subtitleNode.textContent = `${config.pageName ?? "Page"} \u2022 ${suffix}`;
                subtitleNode.classList.add("bugence-editor-modal__subtitle--revamp");
            };

            applySubtitle(state.currentKey ?? null);
            resolveSectionMetadata().then((meta) => applySubtitle(meta?.sectionKey ?? null));
        }

        const toolbar = modal.querySelector(".bugence-editor-toolbar");
        if (toolbar) {
            let metrics = modal.querySelector("[data-bugence-enhanced-words]");
            if (!metrics) {
                const metricsContainer = document.createElement("div");
                metricsContainer.className = "bugence-editor-metrics";
                metricsContainer.innerHTML = `
            <div class="bugence-editor-metric">
                <span class="bugence-editor-metric__value" data-bugence-enhanced-words>0</span>
                <span class="bugence-editor-metric__label">Words</span>
            </div>
            <div class="bugence-editor-metric">
                <span class="bugence-editor-metric__value" data-bugence-enhanced-chars>0</span>
                <span class="bugence-editor-metric__label">Characters</span>
            </div>`;
                toolbar.parentElement.insertBefore(metricsContainer, toolbar.nextSibling);
                updateMetrics(metricsContainer, surface);
                surface.addEventListener("input", () => updateMetrics(metricsContainer, surface));
                surface.addEventListener("keyup", () => updateMetrics(metricsContainer, surface));
            }
        }

        const saveButton = modal.querySelector(".bugence-editor-btn--primary");
        if (saveButton && !saveButton.dataset.bugenceEnhanced) {
            saveButton.dataset.bugenceEnhanced = "true";
            saveButton.addEventListener(
                "click",
                () => {
                    surface.innerHTML = sanitizeHtml(surface.innerHTML);
                    window.setTimeout(fetchSections, 1500);
                },
                { capture: true }
            );
        }

        const actions = modal.querySelector(".bugence-editor-actions");
        if (!actions || actions.querySelector("[data-bugence-delete]")) {
            return;
        }

        const deleteButton = document.createElement("button");
        deleteButton.type = "button";
        deleteButton.className = "bugence-editor-btn bugence-editor-btn--danger";
        deleteButton.dataset.bugenceDelete = "true";
        deleteButton.innerHTML = `<span class="fa-solid fa-trash-can" aria-hidden="true"></span> Delete section`;

        deleteButton.addEventListener("click", async () => {
            try {
                const metadata = await resolveSectionMetadata();
                if (!metadata?.id) {
                    showToast("Unable to delete: refresh and try again.", "error");
                    return;
                }

                if (!window.confirm("Delete this section's content? Publish to push live.")) {
                    return;
                }

                deleteButton.disabled = true;
                await deleteSection(metadata.id);
                surface.innerHTML = "";
                const metricsContainer = modal.querySelector(".bugence-editor-metrics");
                if (metricsContainer) {
                    updateMetrics(metricsContainer, surface);
                }
                showToast("Section cleared. Publish to sync live.", "success");
                const close = modal.querySelector(".bugence-editor-modal__icon");
                close?.click();
            } catch (error) {
                console.error("[Bugence Editor+]", error);
                showToast(error?.message ?? "Unable to delete section.", "error");
                deleteButton.disabled = false;
            }
        });

        actions.prepend(deleteButton);
    };

    const observer = new MutationObserver((mutations) => {
        mutations.forEach((mutation) => {
            mutation.addedNodes.forEach((node) => {
                if (!(node instanceof HTMLElement)) {
                    return;
                }

                if (node.classList.contains("bugence-editor-modal")) {
                    enhanceModal(node);
                } else {
                    const modal = node.querySelector?.(".bugence-editor-modal");
                    if (modal) {
                        enhanceModal(modal);
                    }
                }
            });
        });
    });

    observer.observe(document.body, { childList: true, subtree: true });

    document.addEventListener(
        "dblclick",
        (event) => {
            const element = event.target instanceof Element ? event.target.closest("[data-bugence-section]") : null;
            if (!element) {
                state.currentKey = null;
                state.currentSelector = null;
                return;
            }

            state.currentKey = element.dataset?.bugenceSection ?? null;
            state.currentSelector = computeSelector(element);
        },
        true
    );

    fetchSections();
})();
