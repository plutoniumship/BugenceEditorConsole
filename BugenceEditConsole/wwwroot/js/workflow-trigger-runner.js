(function () {
  if (window.BugenceWorkflowRunner) return;

  const SUBMIT_CLICK_WINDOW_MS = 650;

  const normalizeDguid = (value) => {
    if (!value) return '';
    return String(value).replace(/-/g, '').trim().toLowerCase();
  };

  const post = (payload) => {
    try {
      return fetch('/api/workflows/trigger', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
        keepalive: true
      });
    } catch {
      return null;
    }
  };

  const collectFields = (form) => {
    const fields = {};
    if (!form) return fields;
    const setFieldValue = (key, value, overwrite) => {
      if (!key) return;
      if (!overwrite && Object.prototype.hasOwnProperty.call(fields, key)) return;
      fields[key] = value;
    };
    const elements = Array.from(form.elements || []);
    elements.forEach((el) => {
      if (!el || el.disabled) return;
      const nameKey = (el.name || '').trim();
      const idKey = (el.id || '').trim();
      if (!nameKey && !idKey) return;
      const type = (el.type || '').toLowerCase();
      if (type === 'password' || type === 'file') return;
      if (type === 'checkbox') {
        const value = el.checked ? 'true' : 'false';
        setFieldValue(nameKey, value, true);
        setFieldValue(idKey, value, false);
        return;
      }
      if (type === 'radio') {
        if (el.checked) {
          const value = el.value || '';
          setFieldValue(nameKey, value, true);
          setFieldValue(idKey, value, false);
        }
        return;
      }
      if (el.tagName === 'SELECT' && el.multiple) {
        const value = Array.from(el.selectedOptions || []).map((o) => o.value).join(',');
        setFieldValue(nameKey, value, true);
        setFieldValue(idKey, value, false);
        return;
      }
      const value = el.value || '';
      setFieldValue(nameKey, value, true);
      setFieldValue(idKey, value, false);
    });
    return fields;
  };

  const findEmail = (form, root) => {
    const scope = form || root || document;
    const emailInput = scope.querySelector('input[type="email"], input[name*="email" i], input[id*="email" i]');
    return emailInput ? (emailInput.value || '') : '';
  };

  const getWorkflowRef = (el) => {
    const workflowId = (el.getAttribute('data-bugence-workflow-id') || '').trim();
    const dguid = normalizeDguid(el.getAttribute('data-bugence-workflow-dguid') || '');
    return { workflowId, workflowDguid: dguid };
  };

  const trigger = (el, sourceType) => {
    if (!el) return;
    const ref = getWorkflowRef(el);
    if (!ref.workflowId && !ref.workflowDguid) return;

    const form = el.tagName.toLowerCase() === 'form' ? el : el.closest('form');
    const fields = collectFields(form);
    const email = fields.email || findEmail(form, document);

    post({
      workflowId: ref.workflowId || undefined,
      workflowDguid: ref.workflowDguid || undefined,
      email,
      fields,
      sourceUrl: window.location.href,
      elementTag: (el.tagName || '').toLowerCase(),
      elementId: el.id || '',
      sourceType
    });
  };

  const attach = () => {
    document.querySelectorAll('[data-bugence-workflow-id], [data-bugence-workflow-dguid]').forEach((el) => {
      if (el.dataset.bugenceWorkflowBound === 'true') return;
      el.dataset.bugenceWorkflowBound = 'true';

      const tag = (el.tagName || '').toLowerCase();
      if (tag === 'form') {
        el.addEventListener('submit', () => {
          trigger(el, 'submit');
        });
        return;
      }

      el.addEventListener('click', () => {
        const form = el.closest('form');
        if (form) {
          const type = (el.getAttribute('type') || '').toLowerCase();
          if (tag === 'button' && (type === '' || type === 'submit')) {
            form.dataset.bugenceWorkflowSubmitClickAt = String(Date.now());
            return;
          }
          if (tag === 'input' && type === 'submit') {
            form.dataset.bugenceWorkflowSubmitClickAt = String(Date.now());
            return;
          }
        }
        trigger(el, 'click');
      });
    });

    document.querySelectorAll('form').forEach((form) => {
      if (form.dataset.bugenceWorkflowFormBound === 'true') return;
      form.dataset.bugenceWorkflowFormBound = 'true';

      form.addEventListener('submit', () => {
        const clickedAt = Number(form.dataset.bugenceWorkflowSubmitClickAt || '0');
        const isRecentSubmitClick = clickedAt > 0 && Date.now() - clickedAt <= SUBMIT_CLICK_WINDOW_MS;
        form.dataset.bugenceWorkflowSubmitClickAt = '';

        const taggedForm = form.hasAttribute('data-bugence-workflow-id') || form.hasAttribute('data-bugence-workflow-dguid');
        if (taggedForm) {
          trigger(form, 'submit');
          return;
        }

        if (!isRecentSubmitClick) return;

        const submitTrigger = form.querySelector('[data-bugence-workflow-id], [data-bugence-workflow-dguid]');
        if (submitTrigger) {
          trigger(submitTrigger, 'submit');
        }
      });
    });
  };

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', attach);
  } else {
    attach();
  }

  window.BugenceWorkflowRunner = { attach };
})();
