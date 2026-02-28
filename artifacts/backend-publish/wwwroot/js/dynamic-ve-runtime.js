(function () {
  if (window.BugenceDynamicVeRuntime) return;

  const MEDIA = {
    desktop: null,
    tablet: '(min-width: 768px) and (max-width: 1024px)',
    mobile: '(max-width: 767px)'
  };
  const STATE_SUFFIX = { base: '', hover: ':hover', focus: ':focus', active: ':active' };
  let overlayCache = null;
  let observer = null;
  let reconcileQueued = false;
  const boundEvents = new WeakMap();
  let runtimeConfig = {};
  let affectedElementKeys = new Set();

  const safeParse = (raw, fallback) => {
    try { return JSON.parse(raw); } catch { return fallback; }
  };

  const loadJson = async (url) => {
    const response = await fetch(url, { credentials: 'same-origin' });
    if (!response.ok) throw new Error('Failed to load Dynamic VE overlay.');
    return response.json();
  };

  const emitTrace = (type, payload) => {
    if (!runtimeConfig?.debug) return;
    const trace = {
      type,
      at: new Date().toISOString(),
      payload: payload || {}
    };
    try {
      window.dispatchEvent(new CustomEvent('bugence:dve-trace', { detail: trace }));
    } catch { }
    try {
      if (runtimeConfig.projectId) {
        fetch('/api/dve/trace', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          credentials: 'same-origin',
          body: JSON.stringify({
            projectId: runtimeConfig.projectId,
            revisionId: runtimeConfig.revisionId || null,
            trace
          })
        }).catch(() => { });
      }
    } catch { }
  };

  const ensureWorkflowRunner = () => {
    if (window.BugenceWorkflowRunner) return;
    if (document.querySelector('script[data-bugence-dve-workflow-runner="true"]')) return;
    const script = document.createElement('script');
    script.src = '/js/workflow-trigger-runner.js';
    script.defer = true;
    script.setAttribute('data-bugence-dve-workflow-runner', 'true');
    document.head.appendChild(script);
  };

  const uniqSelectors = (parts) => {
    const list = [];
    const seen = new Set();
    parts.forEach((selector) => {
      const trimmed = (selector || '').trim();
      if (!trimmed || seen.has(trimmed)) return;
      seen.add(trimmed);
      list.push(trimmed);
    });
    return list;
  };

  const selectorCandidates = (entry) => {
    const map = entry?.elementMap || null;
    const mapFallback = Array.isArray(map?.fallbackSelectors) ? map.fallbackSelectors : [];
    const entryFallback = Array.isArray(entry?.fallbackSelectors) ? entry.fallbackSelectors : [];
    return uniqSelectors([entry?.selector, map?.primarySelector, ...entryFallback, ...mapFallback]);
  };

  const resolveElement = (entry) => {
    const candidates = selectorCandidates(entry);
    for (const selector of candidates) {
      try {
        const node = document.querySelector(selector);
        if (node) {
          if (entry?.elementKey) node.setAttribute('data-bugence-node', entry.elementKey);
          if (entry?.elementMap) {
            entry.elementMap.lastResolvedSelector = selector;
            entry.elementMap.lastResolvedAtUtc = new Date().toISOString();
          }
          return node;
        }
      } catch { }
    }
    if (entry?.elementKey) {
      return document.querySelector(`[data-bugence-node="${CSS.escape(entry.elementKey)}"]`);
    }
    return null;
  };

  const buildRuleCss = (overlay) => {
    const byBucket = new Map();
    const rules = Array.isArray(overlay?.rules) ? overlay.rules : [];
    rules.forEach((rule) => {
      const breakpoint = (rule.breakpoint || 'desktop').toLowerCase();
      const state = (rule.state || 'base').toLowerCase();
      const property = (rule.property || '').trim();
      if (!property) return;
      const selectors = selectorCandidates(rule);
      if (!selectors.length) return;
      const selector = selectors[0];
      const bucketKey = `${breakpoint}|${state}|${selector}`;
      if (!byBucket.has(bucketKey)) {
        byBucket.set(bucketKey, { breakpoint, state, selector, props: [] });
      }
      byBucket.get(bucketKey).props.push(`${property}: ${rule.value};`);
    });

    const blocksByBreakpoint = new Map();
    byBucket.forEach((bucket) => {
      const stateSuffix = STATE_SUFFIX[bucket.state] ?? '';
      const line = `${bucket.selector}${stateSuffix}{${bucket.props.join('')}}`;
      if (!blocksByBreakpoint.has(bucket.breakpoint)) blocksByBreakpoint.set(bucket.breakpoint, []);
      blocksByBreakpoint.get(bucket.breakpoint).push(line);
    });

    const cssParts = [];
    blocksByBreakpoint.forEach((blockList, breakpoint) => {
      if (!blockList.length) return;
      const media = MEDIA[breakpoint] ?? null;
      if (!media) {
        cssParts.push(blockList.join('\n'));
      } else {
        cssParts.push(`@media ${media}{${blockList.join('\n')}}`);
      }
    });
    return cssParts.join('\n');
  };

  const applyRules = (overlay) => {
    const styleId = 'bugence-dve-style';
    const existing = document.getElementById(styleId);
    if (existing) existing.remove();
    const css = buildRuleCss(overlay);
    if (!css) return;
    const style = document.createElement('style');
    style.id = styleId;
    style.textContent = css;
    document.head.appendChild(style);
  };

  const applyText = (overlay) => {
    const textPatches = Array.isArray(overlay?.textPatches) ? overlay.textPatches : [];
    textPatches.forEach((item) => {
      const node = resolveElement(item);
      if (!node) return;
      if ((item.textMode || 'plain') === 'plain') node.textContent = item.content || '';
      else node.innerHTML = item.content || '';
      emitTrace('text-applied', { elementKey: item.elementKey || null });
    });
  };

  const applySections = (overlay) => {
    const list = Array.isArray(overlay?.sectionInstances) ? overlay.sectionInstances : [];
    list.forEach((instance) => {
      const instanceId = String(instance.id || `${instance.templateId || 'section'}_${instance.targetElementKey || 'root'}`);
      if (document.querySelector(`[data-bugence-dve-section="${CSS.escape(instanceId)}"]`)) return;

      const markup = safeParse(instance.markupJson || '{}', {});
      const html = markup?.html || '';
      if (!html) return;
      const target = resolveElement(instance) || document.querySelector('main') || document.body;
      if (!target || !target.parentElement) return;

      const template = document.createElement('template');
      template.innerHTML = html;
      const fragment = template.content.cloneNode(true);
      const marker = document.createElement('div');
      marker.setAttribute('data-bugence-dve-section', instanceId);
      marker.style.display = 'none';

      const mode = (instance.insertMode || 'after').toLowerCase();
      if (mode === 'before') {
        target.parentElement.insertBefore(marker, target);
        target.parentElement.insertBefore(fragment, target);
      } else if (mode === 'inside-start') {
        target.prepend(marker);
        target.prepend(fragment);
      } else if (mode === 'inside-end') {
        target.append(marker);
        target.append(fragment);
      } else {
        target.parentElement.insertBefore(marker, target.nextSibling);
        target.parentElement.insertBefore(fragment, marker.nextSibling);
      }
      emitTrace('section-applied', { id: instanceId, mode });
    });
  };

  const setBoundHandler = (node, type, handler) => {
    const existing = boundEvents.get(node) || new Map();
    const previous = existing.get(type);
    if (previous) node.removeEventListener(type, previous, true);
    node.addEventListener(type, handler, true);
    existing.set(type, handler);
    boundEvents.set(node, existing);
  };

  const applyBindings = (overlay) => {
    const list = Array.isArray(overlay?.actionBindings) ? overlay.actionBindings : [];
    let hasWorkflow = false;
    let workflowBindingChanged = false;
    const normalizeTrigger = (value, tag) => {
      const normalized = String(value || 'auto').trim().toLowerCase();
      if (normalized === 'click' || normalized === 'submit') return normalized;
      return tag === 'form' ? 'submit' : 'click';
    };
    list.forEach((binding) => {
      const node = resolveElement(binding);
      if (!node) return;
      emitTrace('binding-resolved', {
        elementKey: binding.elementKey || null,
        selector: binding.selector || null,
        actionType: binding.actionType || null
      });
      const actionType = (binding.actionType || '').toLowerCase();
      const navigateUrl = (binding.navigateUrl || '').trim();
      const workflowId = String(binding.workflowId || '').trim();
      const workflowDguid = String(binding.workflowDguid || '').replace(/-/g, '').trim().toLowerCase();
      const behavior = safeParse(binding.behaviorJson || '{}', {});
      const openInNewTab = Boolean(behavior?.openInNewTab);
      const tag = (node.tagName || '').toLowerCase();
      const eventType = normalizeTrigger(binding.triggerEvent, tag);

      node.setAttribute('data-bugence-action', actionType);
      if ((workflowId || workflowDguid) && (actionType === 'workflow' || actionType === 'hybrid')) {
        if (workflowId) node.setAttribute('data-bugence-workflow-id', workflowId);
        else node.removeAttribute('data-bugence-workflow-id');
        if (workflowDguid) node.setAttribute('data-bugence-workflow-dguid', workflowDguid);
        else node.removeAttribute('data-bugence-workflow-dguid');
        hasWorkflow = true;
        workflowBindingChanged = true;
        setBoundHandler(node, eventType, (event) => {
          if (eventType === 'submit') event.preventDefault();
          emitTrace('workflow-triggered', {
            workflowId,
            workflowDguid,
            actionType,
            triggerEvent: eventType,
            elementKey: binding.elementKey || null
          });
          if (!window.BugenceWorkflowRunner) {
            emitTrace('workflow-trigger-failed', {
              reason: 'runner-missing',
              elementKey: binding.elementKey || null
            });
          }
        });
        emitTrace('workflow-attach', {
          actionType,
          eventType,
          elementKey: binding.elementKey || null,
          workflowId,
          workflowDguid
        });
      }
      if (!navigateUrl || (actionType !== 'navigate' && actionType !== 'hybrid')) return;

      const handler = (event) => {
        if (eventType === 'submit') event.preventDefault();
        emitTrace('navigate-fired', { actionType, navigateUrl, elementKey: binding.elementKey || null });
        if (openInNewTab) window.open(navigateUrl, '_blank', 'noopener');
        else window.location.href = navigateUrl;
      };
      setBoundHandler(node, eventType, handler);
      emitTrace('binding-attached', { actionType, eventType, elementKey: binding.elementKey || null, navigateUrl });
    });

    if (hasWorkflow) {
      ensureWorkflowRunner();
      setTimeout(() => {
        try {
          if (workflowBindingChanged) {
            window.BugenceWorkflowRunner?.attach?.();
          }
        } catch {
          emitTrace('workflow-trigger-failed', { reason: 'attach-exception' });
        }
      }, 200);
    }
  };

  const enrichWithMaps = (overlay) => {
    const elementMaps = Array.isArray(overlay?.elementMaps) ? overlay.elementMaps : [];
    const lookup = new Map();
    elementMaps.forEach((map) => lookup.set(map.elementKey, map));

    const withMap = (item) => ({
      ...item,
      elementMap: lookup.get(item.elementKey || item.targetElementKey || '') || null
    });

    return {
      ...overlay,
      rules: (overlay.rules || []).map(withMap),
      textPatches: (overlay.textPatches || []).map(withMap),
      sectionInstances: (overlay.sectionInstances || []).map((item) => ({
        ...item,
        elementMap: lookup.get(item.targetElementKey || '') || null
      })),
      actionBindings: (overlay.actionBindings || []).map(withMap)
    };
  };

  const applyOverlay = (overlay) => {
    applyRules(overlay);
    applyText(overlay);
    applySections(overlay);
    applyBindings(overlay);
  };

  const applyOverlayForKeys = (overlay, keys) => {
    // Keep style application deterministic and complete.
    // We still collect affected keys for diagnostics and future bucket optimization.
    applyOverlay(overlay);
  };

  const queueReconcile = () => {
    if (reconcileQueued || !overlayCache) return;
    reconcileQueued = true;
    requestAnimationFrame(() => {
      reconcileQueued = false;
      const keys = affectedElementKeys;
      affectedElementKeys = new Set();
      applyOverlayForKeys(overlayCache, keys);
      emitTrace('reconcile', { affected: keys.size });
    });
  };

  const startObserver = () => {
    if (observer) observer.disconnect();
    observer = new MutationObserver((records) => {
      records.forEach((record) => {
        const target = record.target && record.target.nodeType === 1 ? record.target : null;
        if (target) {
          const key = target.getAttribute('data-bugence-node');
          if (key) affectedElementKeys.add(key);
        }
      });
      queueReconcile();
    });
    observer.observe(document.documentElement, {
      childList: true,
      subtree: true,
      attributes: true,
      characterData: false
    });
  };

  const boot = async () => {
    const configNode = document.getElementById('bugence-dve-config');
    if (!configNode) return;
    runtimeConfig = safeParse(configNode.textContent || '{}', {});
    const overlayPath = runtimeConfig.overlayPath || '';
    if (!overlayPath) return;
    const rawOverlay = await loadJson(overlayPath);
    overlayCache = enrichWithMaps(rawOverlay);
    applyOverlay(overlayCache);
    startObserver();
    emitTrace('runtime-boot', { overlayPath });
  };

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => boot().catch(() => { }));
  } else {
    boot().catch(() => { });
  }

  window.BugenceDynamicVeRuntime = { boot, reconcile: queueReconcile };
})();
