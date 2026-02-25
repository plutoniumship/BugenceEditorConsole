// @ts-nocheck
import { initInsightsAnalytics } from "@bugence/analytics";
import { initSchemaSidebar } from "./ui/schemaSidebar";
import { initTimelinePanel } from "./ui/timelinePanel";
import { initLibraryConsole } from "./ui/libraryConsole";
import { initSectionDetails } from "./ui/sectionDetails";
import { initPublishConsole } from "./ui/publishConsole";
import { initDashboardNotifications } from "./ui/dashboardNotifications";
(() => {
    const initProfileMenus = () => {
        const menus = Array.from(document.querySelectorAll('[data-profile-menu]'));
        if (!menus.length) {
            return;
        }
        const toggleSelector = '[data-profile-toggle]';
        let activeMenu = null;
        const findMenu = (element) => element ? menus.find((menu) => menu.contains(element)) || null : null;
        const setMenuState = (menu, isOpen) => {
            const trigger = menu.querySelector(toggleSelector);
            if (trigger) {
                trigger.setAttribute('aria-expanded', isOpen ? 'true' : 'false');
            }
            menu.dataset.open = isOpen ? 'true' : 'false';
        };
        const closeMenu = (menu) => {
            if (!menu) {
                return;
            }
            setMenuState(menu, false);
            if (activeMenu === menu) {
                activeMenu = null;
            }
        };
        const openMenu = (menu) => {
            if (activeMenu && activeMenu !== menu) {
                closeMenu(activeMenu);
            }
            setMenuState(menu, true);
            activeMenu = menu;
        };
        const focusFirstAction = (menu) => {
            const firstAction = menu.querySelector('[role="menuitem"]');
            if (firstAction) {
                firstAction.focus({ preventScroll: true });
            }
        };
        const isToggle = (element) => !!element?.closest(toggleSelector);
        document.addEventListener('click', (event) => {
            const toggle = event.target.closest(toggleSelector);
            if (toggle) {
                const menu = findMenu(toggle);
                if (!menu) {
                    return;
                }
                event.preventDefault();
                const isOpen = menu.dataset.open === 'true';
                if (isOpen) {
                    closeMenu(menu);
                }
                else {
                    openMenu(menu);
                }
                return;
            }
            if (activeMenu && !activeMenu.contains(event.target)) {
                closeMenu(activeMenu);
            }
        });
        document.addEventListener('keydown', (event) => {
            const target = event.target;
            if (target instanceof HTMLElement) {
                if (target.isContentEditable || target.matches('input, textarea, select')) {
                    return;
                }
            }
            if (isToggle(target)) {
                const menu = findMenu(target);
                if (!menu) {
                    return;
                }
                if (event.key === 'Enter' || event.key === ' ') {
                    event.preventDefault();
                    target.click();
                }
                if (event.key === 'ArrowDown') {
                    event.preventDefault();
                    if (menu.dataset.open !== 'true') {
                        openMenu(menu);
                    }
                    focusFirstAction(menu);
                }
                if (event.key === 'Escape') {
                    event.preventDefault();
                    closeMenu(menu);
                }
                return;
            }
            if (event.key === 'Escape' && activeMenu) {
                const trigger = activeMenu.querySelector(toggleSelector);
                closeMenu(activeMenu);
                if (trigger) {
                    trigger.focus({ preventScroll: true });
                }
            }
        });
        document.addEventListener('focusin', (event) => {
            if (activeMenu && !activeMenu.contains(event.target)) {
                closeMenu(activeMenu);
            }
        });
        window.addEventListener('blur', () => {
            if (activeMenu) {
                closeMenu(activeMenu);
            }
        });
    };
    const initNavDropdowns = () => {
        const dropdowns = Array.from(document.querySelectorAll('[data-nav-dropdown]'));
        if (!dropdowns.length) {
            return;
        }
        const closeAll = (exception = null) => {
            dropdowns.forEach((dropdown) => {
                const toggle = dropdown.querySelector('[data-nav-dropdown-toggle]');
                const shouldRemainOpen = dropdown === exception;
                dropdown.dataset.open = shouldRemainOpen ? 'true' : 'false';
                if (toggle) {
                    toggle.setAttribute('aria-expanded', shouldRemainOpen ? 'true' : 'false');
                }
            });
        };
        dropdowns.forEach((dropdown) => {
            const toggle = dropdown.querySelector('[data-nav-dropdown-toggle]');
            if (!toggle) {
                return;
            }
            const menu = dropdown.querySelector('[data-nav-dropdown-menu]');
            const setState = (isOpen) => {
                dropdown.dataset.open = isOpen ? 'true' : 'false';
                toggle.setAttribute('aria-expanded', isOpen ? 'true' : 'false');
            };
            toggle.addEventListener('click', (event) => {
                event.preventDefault();
                const isOpen = dropdown.dataset.open === 'true';
                if (isOpen) {
                    setState(false);
                }
                else {
                    closeAll(dropdown);
                    setState(true);
                }
            });
            dropdown.addEventListener('keydown', (event) => {
                if (event.key === 'Escape') {
                    setState(false);
                    toggle.focus({ preventScroll: true });
                }
            });
            if (menu) {
                menu.querySelectorAll('a, button').forEach((item) => {
                    item.addEventListener('click', () => {
                        setState(false);
                    });
                });
            }
        });
        document.addEventListener('click', (event) => {
            if (dropdowns.some((dropdown) => dropdown.contains(event.target))) {
                return;
            }
            closeAll();
        });
        closeAll();
    };
    const initRichEditors = () => {
        const editors = Array.from(document.querySelectorAll('[data-editor]'));
        if (!editors.length) {
            return;
        }
        const ALLOWED_STYLE_PROPERTIES = new Set([
            'color',
            'background-color',
            'font-size',
            'font-family',
            'font-style',
            'font-weight',
            'font-variant',
            'text-transform',
            'text-decoration',
            'text-decoration-line',
            'letter-spacing',
            'line-height'
        ]);
        const sanitizeStyleAttribute = (value) => {
            if (!value) {
                return '';
            }
            const sanitized = value
                .split(';')
                .map((segment) => segment.trim())
                .filter(Boolean)
                .map((segment) => {
                const [rawProperty, ...rawValueParts] = segment.split(':');
                if (!rawProperty || rawValueParts.length === 0) {
                    return null;
                }
                const property = rawProperty.trim().toLowerCase();
                if (!ALLOWED_STYLE_PROPERTIES.has(property)) {
                    return null;
                }
                const rawValue = rawValueParts.join(':').trim();
                if (!rawValue ||
                    /url\s*\(/i.test(rawValue) ||
                    /expression/i.test(rawValue) ||
                    /javascript:/i.test(rawValue)) {
                    return null;
                }
                return `${property}: ${rawValue}`;
            })
                .filter(Boolean)
                .join('; ');
            return sanitized;
        };
        const sanitizeHtml = (input) => {
            const template = document.createElement('template');
            template.innerHTML = input;
            template.content.querySelectorAll('script, style').forEach((node) => node.remove());
            template.content.querySelectorAll('*').forEach((node) => {
                Array.from(node.attributes).forEach((attr) => {
                    if (attr.name.startsWith('on')) {
                        node.removeAttribute(attr.name);
                        return;
                    }
                    if (attr.name === 'style') {
                        const cleaned = sanitizeStyleAttribute(attr.value);
                        if (cleaned) {
                            node.setAttribute('style', cleaned);
                        }
                        else {
                            node.removeAttribute('style');
                        }
                    }
                });
            });
            return template.innerHTML;
        };
        const escapeHtml = (value) => {
            if (typeof value !== 'string') {
                return '';
            }
            return value
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;')
                .replace(/'/g, '&#39;');
        };
        const editorInstances = [];
        const processedForms = new WeakSet();
        const setupEditor = (editor) => {
            const mode = editor.dataset.editorMode || 'text';
            const surface = editor.querySelector('[data-editor-surface]');
            const storage = editor.querySelector('[data-editor-input]');
            const codeArea = editor.querySelector('[data-editor-code]');
            const metricsRoot = editor.querySelector('[data-editor-metrics]');
            const wordsMetric = metricsRoot ? metricsRoot.querySelector('[data-editor-words]') : null;
            const charsMetric = metricsRoot ? metricsRoot.querySelector('[data-editor-chars]') : null;
            const readMetric = metricsRoot ? metricsRoot.querySelector('[data-editor-read]') : null;
            const fontFamilySelect = surface ? editor.querySelector('[data-editor-font-family]') : null;
            const fontSizeSelect = surface ? editor.querySelector('[data-editor-font-size]') : null;
            const styleButtons = surface
                ? Array.from(editor.querySelectorAll('[data-style-lineheight], [data-style-letterspacing], [data-style-wordspacing], [data-style-texttransform]'))
                : [];
            const fontStacks = {
                norwester: '"Norwester", sans-serif',
                inter: '"Inter", "Segoe UI", sans-serif',
                playfair: '"Playfair Display", "Times New Roman", serif',
                space: '"Space Grotesk", "Futura", sans-serif',
                lora: '"Lora", "Georgia", serif',
                work: '"Work Sans", "Helvetica Neue", sans-serif',
                mono: '"IBM Plex Mono", "SFMono-Regular", monospace'
            };
            const buildSummarySnippet = (value) => {
                const normalized = (value || '')
                    .replace(/\s+/g, ' ')
                    .trim();
                if (!normalized) {
                    return '';
                }
                if (normalized.length <= 80) {
                    return normalized;
                }
                return `${normalized.slice(0, 77).trimEnd()}...`;
            };
            const ensureCustomOption = (select, value) => {
                if (!select) {
                    return;
                }
                let existing = select.querySelector('option[data-custom="true"]');
                if (!value) {
                    if (existing) {
                        existing.remove();
                    }
                    if (select.value === '__custom__') {
                        select.value = '';
                    }
                    return;
                }
                if (!existing) {
                    existing = document.createElement('option');
                    existing.dataset.custom = 'true';
                    select.appendChild(existing);
                }
                existing.value = value;
                existing.textContent = `Custom (${value})`;
                select.value = value;
            };
            const selectionWithinSurface = () => {
                if (!surface) {
                    return null;
                }
                const selection = window.getSelection();
                if (!selection || selection.rangeCount === 0) {
                    return null;
                }
                const range = selection.getRangeAt(0);
                if (!surface.contains(range.commonAncestorContainer)) {
                    return null;
                }
                const anchor = selection.focusNode || selection.anchorNode;
                let contextElement = anchor instanceof Element ? anchor : anchor?.parentElement || surface;
                if (!surface.contains(contextElement)) {
                    contextElement = surface;
                }
                return { selection, range, contextElement };
            };
            const wrapSelectionWithStyles = (styles) => {
                const context = selectionWithinSurface();
                if (!context) {
                    return;
                }
                const { selection, range } = context;
                const span = document.createElement('span');
                Object.entries(styles).forEach(([key, value]) => {
                    if (value !== undefined && value !== null) {
                        span.style[key] = value;
                    }
                });
                try {
                    if (range.collapsed) {
                        span.appendChild(document.createTextNode(''));
                        range.insertNode(span);
                    }
                    else {
                        const fragment = range.extractContents();
                        span.appendChild(fragment);
                        range.insertNode(span);
                    }
                }
                catch {
                    const container = document.createElement('div');
                    container.appendChild(range.cloneContents());
                    span.innerHTML = container.innerHTML || selection.toString() || '';
                    document.execCommand('insertHTML', false, span.outerHTML);
                    return;
                }
                selection.removeAllRanges();
                const newRange = document.createRange();
                newRange.selectNodeContents(span);
                if (range.collapsed) {
                    newRange.collapse(true);
                }
                selection.addRange(newRange);
            };
            const markActiveStyleButton = (button) => {
                if (!button) {
                    return;
                }
                const controls = button.closest('.rich-editor__toolbar-controls');
                if (controls) {
                    controls.querySelectorAll('[aria-pressed]').forEach((candidate) => {
                        candidate.setAttribute('aria-pressed', candidate === button ? 'true' : 'false');
                    });
                }
                else {
                    button.setAttribute('aria-pressed', 'true');
                }
            };
            const isDefaultStyleButton = (button) => {
                const data = button.dataset || {};
                const hasLine = Object.prototype.hasOwnProperty.call(data, 'styleLineheight');
                const hasLetter = Object.prototype.hasOwnProperty.call(data, 'styleLetterspacing');
                const hasWord = Object.prototype.hasOwnProperty.call(data, 'styleWordspacing');
                const hasTransform = Object.prototype.hasOwnProperty.call(data, 'styleTexttransform');
                return ((!hasLine || !data.styleLineheight) &&
                    (!hasLetter || !data.styleLetterspacing) &&
                    (!hasWord || !data.styleWordspacing) &&
                    (!hasTransform || data.styleTexttransform === 'none'));
            };
            const applyStyleButton = (button) => {
                if (!surface) {
                    return;
                }
                surface.focus({ preventScroll: true });
                const styles = {};
                if (Object.prototype.hasOwnProperty.call(button.dataset, 'styleLineheight')) {
                    styles.lineHeight = button.dataset.styleLineheight || 'normal';
                }
                if (Object.prototype.hasOwnProperty.call(button.dataset, 'styleLetterspacing')) {
                    styles.letterSpacing = button.dataset.styleLetterspacing || 'normal';
                }
                if (Object.prototype.hasOwnProperty.call(button.dataset, 'styleWordspacing')) {
                    styles.wordSpacing = button.dataset.styleWordspacing || 'normal';
                }
                if (Object.prototype.hasOwnProperty.call(button.dataset, 'styleTexttransform')) {
                    styles.textTransform = button.dataset.styleTexttransform || 'none';
                }
                wrapSelectionWithStyles(styles);
                updateStorage();
                markActiveStyleButton(button);
                syncTypographyControls();
            };
            if (styleButtons.length) {
                styleButtons.forEach((button) => {
                    const defaultState = isDefaultStyleButton(button);
                    button.setAttribute('aria-pressed', defaultState ? 'true' : 'false');
                    button.addEventListener('click', () => {
                        applyStyleButton(button);
                    });
                });
            }
            const resolveFontKey = (fontFamily) => {
                if (!fontFamily) {
                    return '';
                }
                const primary = fontFamily.replace(/["']/g, '').split(',')[0].trim().toLowerCase();
                if (primary === 'norwester') {
                    return 'norwester';
                }
                if (primary === 'inter' || primary === 'segoe ui' || primary === 'helvetica neue') {
                    return 'inter';
                }
                if (primary === 'playfair display' || primary === 'times new roman') {
                    return 'playfair';
                }
                if (primary === 'space grotesk' || primary === 'futura') {
                    return 'space';
                }
                if (primary === 'lora' || primary === 'georgia') {
                    return 'lora';
                }
                if (primary === 'work sans' || primary === 'helvetica') {
                    return 'work';
                }
                if (primary === 'ibm plex mono' || primary === 'sfmono-regular' || primary === 'courier new' || primary === 'monaco') {
                    return 'mono';
                }
                return '';
            };
            const syncTypographyControls = () => {
                if (!surface) {
                    return;
                }
                const context = selectionWithinSurface();
                if (!context) {
                    if (fontFamilySelect) {
                        fontFamilySelect.value = '';
                    }
                    if (fontSizeSelect) {
                        fontSizeSelect.value = '';
                        ensureCustomOption(fontSizeSelect, '');
                    }
                    return;
                }
                const computed = window.getComputedStyle(context.contextElement);
                if (fontFamilySelect) {
                    fontFamilySelect.value = resolveFontKey(computed.fontFamily);
                }
                if (fontSizeSelect) {
                    const sizeValue = (computed.fontSize || '').trim();
                    const knownValues = Array.from(fontSizeSelect.options)
                        .map((option) => option.value)
                        .filter((value) => value && value !== '__custom__');
                    if (sizeValue && knownValues.includes(sizeValue)) {
                        fontSizeSelect.value = sizeValue;
                        ensureCustomOption(fontSizeSelect, '');
                    }
                    else if (sizeValue) {
                        ensureCustomOption(fontSizeSelect, sizeValue);
                    }
                    else {
                        fontSizeSelect.value = '';
                        ensureCustomOption(fontSizeSelect, '');
                    }
                }
            };
            const applyFontFamily = (key) => {
                if (!surface) {
                    return;
                }
                const stack = key ? fontStacks[key] ?? key : 'inherit';
                wrapSelectionWithStyles({ fontFamily: stack });
                updateStorage();
                syncTypographyControls();
            };
            const applyFontSize = (value) => {
                if (!surface) {
                    return;
                }
                const resolved = value ? value : 'inherit';
                wrapSelectionWithStyles({ fontSize: resolved });
                updateStorage();
                syncTypographyControls();
            };
            if (!storage) {
                return;
            }
            const normalizeText = () => {
                if (!surface) {
                    return '';
                }
                return surface.innerText
                    .replace(/\u00a0/g, ' ')
                    .replace(/\r/g, '')
                    .replace(/\n{3,}/g, '\n\n')
                    .trim();
            };
            const formatNumber = (value) => {
                try {
                    return new Intl.NumberFormat().format(value);
                }
                catch {
                    return `${value}`;
                }
            };
            const updateMetrics = (snapshot) => {
                if (!metricsRoot) {
                    return;
                }
                const plain = snapshot?.plain || '';
                const words = plain
                    ? plain.trim().split(/\s+/).filter(Boolean).length
                    : 0;
                const chars = plain.replace(/\s/g, '').length;
                const readingMinutes = words / 200;
                let readingLabel = '0 min read';
                if (words > 0) {
                    if (readingMinutes < 1) {
                        readingLabel = '<1 min read';
                    }
                    else {
                        readingLabel = `${Math.max(1, Math.round(readingMinutes))} min read`;
                    }
                }
                if (wordsMetric) {
                    wordsMetric.textContent = `${formatNumber(words)} ${words === 1 ? 'word' : 'words'}`;
                }
                if (charsMetric) {
                    charsMetric.textContent = `${formatNumber(chars)} ${chars === 1 ? 'character' : 'characters'}`;
                }
                if (readMetric) {
                    readMetric.textContent = readingLabel;
                }
            };
            const buildSnapshot = () => {
                if (mode === 'text') {
                    const plain = normalizeText();
                    return {
                        plain,
                        html: escapeHtml(plain).replace(/(?:\r\n|\r|\n)/g, '<br>')
                    };
                }
                if (editor.dataset.code === 'true' && codeArea) {
                    const sanitized = sanitizeHtml(codeArea.value.trim());
                    return {
                        plain: sanitized.replace(/<[^>]+>/g, ' ').replace(/\s+/g, ' ').trim(),
                        html: sanitized
                    };
                }
                if (surface) {
                    const rawHtml = surface.innerHTML
                        .replace(/<div><br><\/div>/gi, '<br>')
                        .replace(/<p><br><\/p>/gi, '<br>');
                    const sanitized = sanitizeHtml(rawHtml).trim();
                    return {
                        plain: surface.innerText
                            .replace(/\u00a0/g, ' ')
                            .replace(/\r/g, '')
                            .replace(/\s+\n/g, '\n')
                            .trim(),
                        html: sanitized
                    };
                }
                return { plain: '', html: '' };
            };
            const updateStorage = () => {
                const snapshot = buildSnapshot();
                if (mode === 'text') {
                    storage.value = snapshot.plain;
                }
                else {
                    storage.value = snapshot.html;
                }
                editor.dispatchEvent(new CustomEvent('rich-editor-change', {
                    bubbles: true,
                    detail: {
                        editor,
                        mode,
                        plainText: snapshot.plain,
                        html: snapshot.html
                    }
                }));
                updateMetrics(snapshot);
                if (surface) {
                    syncTypographyControls();
                }
            };
            if (surface) {
                surface.addEventListener('input', updateStorage);
                surface.addEventListener('blur', () => {
                    if (surface.innerText.trim() === '') {
                        surface.innerHTML = '';
                    }
                    updateStorage();
                    syncTypographyControls();
                });
                surface.addEventListener('keyup', syncTypographyControls);
                surface.addEventListener('mouseup', syncTypographyControls);
                if (!editor.__selectionListener) {
                    const monitorSelection = () => {
                        const selection = window.getSelection();
                        if (!selection || selection.rangeCount === 0) {
                            return;
                        }
                        const anchor = selection.focusNode || selection.anchorNode;
                        if (anchor && surface.contains(anchor)) {
                            syncTypographyControls();
                        }
                    };
                    editor.__selectionListener = monitorSelection;
                    document.addEventListener('selectionchange', monitorSelection);
                }
            }
            if (fontFamilySelect) {
                fontFamilySelect.addEventListener('change', (event) => {
                    applyFontFamily(event.target.value);
                });
            }
            if (fontSizeSelect) {
                fontSizeSelect.addEventListener('change', (event) => {
                    const selected = event.target.value;
                    if (selected === '__custom__') {
                        const customValue = window.prompt('Enter a CSS font-size (e.g. 18px, 1.25rem, clamp(1rem, 2vw, 2rem))');
                        if (!customValue || !customValue.trim()) {
                            fontSizeSelect.value = '';
                            ensureCustomOption(fontSizeSelect, '');
                            return;
                        }
                        const trimmed = customValue.trim();
                        ensureCustomOption(fontSizeSelect, trimmed);
                        applyFontSize(trimmed);
                        return;
                    }
                    ensureCustomOption(fontSizeSelect, '');
                    applyFontSize(selected);
                });
            }
            if (codeArea) {
                codeArea.addEventListener('input', updateStorage);
            }
            const execCommand = (command, value) => {
                if (command === 'toggle-code' && codeArea) {
                    const isActive = editor.dataset.code === 'true';
                    if (!isActive) {
                        codeArea.value = surface ? surface.innerHTML.trim() : storage.value;
                        editor.dataset.code = 'true';
                        codeArea.focus({ preventScroll: true });
                    }
                    else {
                        editor.dataset.code = 'false';
                        if (surface) {
                            surface.innerHTML = sanitizeHtml(codeArea.value);
                            surface.focus({ preventScroll: true });
                        }
                    }
                    updateStorage();
                    return;
                }
                if (!surface) {
                    return;
                }
                if (command === 'createLink') {
                    let url = window.prompt('Enter URL');
                    if (!url) {
                        document.execCommand('unlink');
                        updateStorage();
                        return;
                    }
                    url = url.trim();
                    if (url && !/^https?:\/\//i.test(url)) {
                        url = `https://${url}`;
                    }
                    surface.focus({ preventScroll: true });
                    document.execCommand('createLink', false, url);
                    updateStorage();
                    return;
                }
                let resolvedCommand = command;
                if (resolvedCommand === 'HiliteColor' && !document.queryCommandSupported('HiliteColor')) {
                    resolvedCommand = 'backColor';
                }
                surface.focus({ preventScroll: true });
                if (resolvedCommand === 'removeFormat') {
                    document.execCommand('removeFormat');
                    document.execCommand('unlink');
                    updateStorage();
                    return;
                }
                if (resolvedCommand === 'formatBlock') {
                    document.execCommand('formatBlock', false, value || 'p');
                }
                else {
                    document.execCommand(resolvedCommand, false, value);
                }
                updateStorage();
            };
            editor.querySelectorAll('[data-command]').forEach((button) => {
                button.addEventListener('click', () => {
                    execCommand(button.dataset.command, button.dataset.value);
                });
            });
            const previewTarget = editor.dataset.previewTarget;
            if (previewTarget) {
                const summaryElement = document.querySelector(`[data-preview-tab="${previewTarget}"] [data-preview-summary]`);
                editor.addEventListener('rich-editor-change', (event) => {
                    const target = document.querySelector(`[data-preview-section="${previewTarget}"]`);
                    if (!target) {
                        return;
                    }
                    const detail = event.detail || {};
                    const container = target.closest('.preview-canvas__section');
                    target.innerHTML = detail.html || '';
                    const isEmpty = !detail.plainText && !detail.html;
                    if (isEmpty) {
                        target.classList.add('preview-canvas__content--empty');
                        if (container) {
                            container.classList.add('preview-canvas__section--empty');
                        }
                    }
                    else {
                        target.classList.remove('preview-canvas__content--empty');
                        if (container) {
                            container.classList.remove('preview-canvas__section--empty');
                        }
                    }
                    target.classList.add('preview-canvas__content--dirty');
                    if (container) {
                        container.classList.add('preview-canvas__section--dirty');
                    }
                    if (target.__dirtyTimeout) {
                        window.clearTimeout(target.__dirtyTimeout);
                    }
                    if (container && container.__dirtyTimeout) {
                        window.clearTimeout(container.__dirtyTimeout);
                    }
                    target.__dirtyTimeout = window.setTimeout(() => {
                        target.classList.remove('preview-canvas__content--dirty');
                    }, 700);
                    if (container) {
                        container.__dirtyTimeout = window.setTimeout(() => {
                            container.classList.remove('preview-canvas__section--dirty');
                        }, 700);
                    }
                    if (summaryElement) {
                        const snippetSource = detail.plainText && detail.plainText.trim()
                            ? detail.plainText
                            : detail.html
                                ? detail.html.replace(/<[^>]+>/g, ' ')
                                : '';
                        summaryElement.textContent = buildSummarySnippet(snippetSource);
                    }
                });
            }
            const form = editor.closest('form');
            editorInstances.push({ editor, form, updateStorage });
            if (form && !processedForms.has(form)) {
                processedForms.add(form);
                form.addEventListener('submit', () => {
                    editorInstances
                        .filter((instance) => instance.form === form)
                        .forEach((instance) => instance.updateStorage());
                });
            }
            updateStorage();
            syncTypographyControls();
        };
        editors.forEach(setupEditor);
    };
    const initSectionFilters = () => {
        const sectionsRoot = document.querySelector('[data-editor-sections]');
        if (!sectionsRoot) {
            return;
        }
        const cards = Array.from(sectionsRoot.querySelectorAll('[data-editor-card]'));
        if (!cards.length) {
            return;
        }
        const searchInput = document.querySelector('[data-editor-search]');
        const filterCount = document.querySelector('[data-editor-filter-count]');
        const toggles = Array.from(document.querySelectorAll('[data-editor-filter]'));
        const activeFilters = new Set();
        const applyFilters = () => {
            const query = (searchInput?.value || '').trim().toLowerCase();
            const visibleCards = [];
            cards.forEach((card) => {
                let isVisible = true;
                const status = card.dataset.sectionStatus || '';
                const hasDraft = card.dataset.hasDraft === 'true';
                const keywords = `${card.dataset.sectionTitle ?? ''} ${card.dataset.sectionKey ?? ''}`.toLowerCase();
                if (query && !keywords.includes(query)) {
                    isVisible = false;
                }
                if (isVisible && activeFilters.has('editable') && status !== 'editable') {
                    isVisible = false;
                }
                if (isVisible && activeFilters.has('draft') && !hasDraft) {
                    isVisible = false;
                }
                card.hidden = !isVisible;
                if (isVisible) {
                    visibleCards.push(card);
                }
            });
            if (filterCount) {
                const total = cards.length;
                filterCount.textContent = visibleCards.length === total
                    ? `${total} sections`
                    : `${visibleCards.length} of ${total} sections`;
            }
        };
        if (searchInput) {
            searchInput.addEventListener('input', () => {
                window.requestAnimationFrame(applyFilters);
            });
        }
        toggles.forEach((toggle) => {
            toggle.addEventListener('click', () => {
                const key = toggle.dataset.editorFilter;
                if (!key) {
                    return;
                }
                if (activeFilters.has(key)) {
                    activeFilters.delete(key);
                    toggle.dataset.state = 'off';
                }
                else {
                    activeFilters.add(key);
                    toggle.dataset.state = 'on';
                }
                applyFilters();
            });
        });
        applyFilters();
    };
    const init = () => {
        initProfileMenus();
        initNavDropdowns();
        initRichEditors();
        initSectionFilters();
        initLibraryConsole();
        initSchemaSidebar();
        initTimelinePanel();
        initSectionDetails();
        initPublishConsole();
        initDashboardNotifications();
        initInsightsAnalytics();
    };
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    }
    else {
        init();
    }
})();
