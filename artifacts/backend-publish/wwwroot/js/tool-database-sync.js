(function () {
  "use strict";

  const ensureStyle = () => {
    if (document.getElementById("toolDbSyncStyle")) return;
    const style = document.createElement("style");
    style.id = "toolDbSyncStyle";
    style.textContent = ".db-sync-btn{display:inline-flex;align-items:center;gap:8px;padding:10px 12px;border-radius:10px;border:1px solid rgba(148,163,184,.25);background:rgba(255,255,255,.03);color:#e2e8f0;font-weight:700;cursor:pointer}.db-sync-btn:hover{border-color:#67e8f9;color:#fff}.db-sync-btn[disabled]{opacity:.65;cursor:not-allowed}";
    document.head.appendChild(style);
  };

  const findById = (id) => (id ? document.getElementById(id) : null);

  const postSync = async (endpoint, requestVerificationToken) => {
    const headers = { "Content-Type": "application/json" };
    if (requestVerificationToken) {
      headers.RequestVerificationToken = requestVerificationToken;
      headers["X-CSRF-TOKEN"] = requestVerificationToken;
    }
    const response = await fetch(endpoint, {
      method: "POST",
      credentials: "include",
      headers,
      body: "{}"
    });
    let payload = null;
    try {
      payload = await response.json();
    } catch {
      payload = null;
    }
    if (!response.ok || payload?.success === false) {
      throw new Error(payload?.message || "Database sync failed.");
    }
    return payload;
  };

  const mountBtn = (anchorEl, text, clickHandler) => {
    if (!anchorEl) return null;
    const btn = document.createElement("button");
    btn.type = "button";
    btn.className = "db-sync-btn";
    btn.innerHTML = "<i class=\"fa-solid fa-arrows-rotate\"></i> " + (text || "Database Sync");
    btn.addEventListener("click", clickHandler);
    anchorEl.insertAdjacentElement("beforebegin", btn);
    return btn;
  };

  const readIdFromElement = (id) => {
    const el = findById(id);
    if (!el) return "";
    if ("value" in el && typeof el.value === "string" && el.value.trim()) return el.value.trim();
    return (el.textContent || "").trim();
  };

  const findEditButtonById = (selector, id) => {
    const targets = document.querySelectorAll(selector || ".edit-viewer,.edit-query,.edit-master,.edit-page,.edit-portlet,.edit-record,.edit-table,.icon-btn.edit");
    for (const target of targets) {
      const data = target.dataset || {};
      const values = Object.values(data);
      if (values.some((v) => String(v || "").trim() === id)) return target;
    }
    return null;
  };

  window.BugenceToolDbSync = {
    init(options) {
      if (!options || !options.endpoint) return;
      ensureStyle();

      const endpoint = options.endpoint;
      const token = options.requestVerificationToken || "";
      const reloadAfterSync = options.reloadAfterSync !== false;
      const reopenKey = "bugence-db-sync-reopen";

      try {
        const pendingRaw = localStorage.getItem(reopenKey);
        if (pendingRaw) {
          const pending = JSON.parse(pendingRaw);
          if (pending?.path === window.location.pathname && pending?.id) {
            localStorage.removeItem(reopenKey);
            setTimeout(() => {
              const btn = findEditButtonById(options.editRowButtonSelector, String(pending.id));
              btn?.click();
            }, 180);
          }
        }
      } catch {
      }

      const handleSync = async (button, isEditSync) => {
        if (!button) return;
        if (button.disabled) return;
        const original = button.innerHTML;
        button.disabled = true;
        button.innerHTML = "<i class=\"fa-solid fa-spinner fa-spin\"></i> Syncing...";
        const editId = isEditSync
          ? (typeof options.getEditRecordId === "function"
            ? String(options.getEditRecordId() || "").trim()
            : readIdFromElement(options.editRecordIdElementId))
          : "";
        try {
          await postSync(endpoint, token);
          if (reloadAfterSync) {
            if (isEditSync && editId) {
              try {
                localStorage.setItem(reopenKey, JSON.stringify({ path: window.location.pathname, id: editId }));
              } catch {
              }
            }
            window.location.reload();
            return;
          }
          if (typeof options.onSuccess === "function") {
            options.onSuccess();
          }
        } catch (error) {
          const message = error?.message || "Database sync failed.";
          if (typeof options.onError === "function") {
            options.onError(message);
          } else {
            alert(message);
          }
        } finally {
          button.disabled = false;
          button.innerHTML = original;
        }
      };

      const topAnchor = findById(options.topButtonAnchorId);
      const topBtn = mountBtn(topAnchor, options.topButtonText || "Database Sync", () => handleSync(topBtn, false));

      const editAnchor = findById(options.editButtonAnchorId);
      const editBtn = mountBtn(editAnchor, options.editButtonText || "Database Sync", () => handleSync(editBtn, true));

      return { topBtn, editBtn };
    }
  };
})();
