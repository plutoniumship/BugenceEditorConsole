(function () {
  "use strict";

  const $id = (id) => document.getElementById(id);
  const esc = (v) => String(v ?? "").replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;").replaceAll("\"", "&quot;").replaceAll("'", "&#39;");
  const token = (scope) => document.querySelector((scope ? `${scope} ` : "") + "input[name='__RequestVerificationToken']")?.value || "";
  const msg = (p, fb) => p?.message || p?.Message || p?.detail || fb;
  const jsonReq = async (url, opts) => {
    const r = await fetch(url, { credentials: "same-origin", ...(opts || {}) });
    const ct = r.headers.get("content-type") || "";
    let p = null;
    try { p = ct.includes("json") ? await r.json() : { message: await r.text() }; } catch { p = null; }
    return { r, p };
  };

  let toastStyle = false;
  const toast = (m, t = "info") => {
    if (!toastStyle) {
      toastStyle = true;
      const s = document.createElement("style");
      s.textContent = ".console-toast-host{position:fixed;right:20px;bottom:20px;display:flex;flex-direction:column;gap:8px;z-index:20000}.console-toast{background:#0b0e14;border:1px solid rgba(148,163,184,.24);color:#e5e7eb;border-radius:10px;padding:10px 12px;min-width:230px;max-width:420px;box-shadow:0 16px 30px rgba(0,0,0,.45);font-size:.86rem}.console-toast.success{border-color:rgba(16,185,129,.45)}.console-toast.warning{border-color:rgba(245,158,11,.45)}.console-toast.error{border-color:rgba(239,68,68,.45)}";
      document.head.appendChild(s);
    }
    let host = $id("consoleToastHost");
    if (!host) {
      host = document.createElement("div");
      host.id = "consoleToastHost";
      host.className = "console-toast-host";
      document.body.appendChild(host);
    }
    const d = document.createElement("div");
    d.className = `console-toast ${t}`;
    d.innerHTML = esc(m);
    host.appendChild(d);
    setTimeout(() => { d.style.opacity = "0"; d.style.transform = "translateY(6px)"; setTimeout(() => d.remove(), 180); }, 3200);
  };
  window.showToast = toast;

  function initGlobalSettings() {
    if (!document.body.classList.contains("page-settings-globalsettings")) return;
    window.showTab = (tab, el) => {
      document.querySelectorAll(".settings-content").forEach((x) => x.classList.add("hidden"));
      $id(`tab-${tab}`)?.classList.remove("hidden");
      document.querySelectorAll(".settings-link").forEach((x) => x.classList.remove("active"));
      el?.classList?.add("active");
    };
    window.updateProjectContext = () => {
      const s = $id("projectSelect");
      if (!s) return;
      const name = s.options[s.selectedIndex]?.text || s.value;
      const slug = name.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "");
      if ($id("headerProjectName")) $id("headerProjectName").textContent = name;
      if ($id("inpName")) $id("inpName").value = name;
      if ($id("inpSlug")) $id("inpSlug").value = slug;
    };
    $id("projectSelect")?.addEventListener("change", window.updateProjectContext);
    window.updateProjectContext();
  }

  function initUpdates() {
    if (!document.body.classList.contains("page-support-updatesannouncements")) return;
    window.filterLogs = (type, btn) => {
      document.querySelectorAll(".filter-chip").forEach((c) => c.classList.remove("active"));
      btn?.classList?.add("active");
      document.querySelectorAll(".log-entry").forEach((e) => e.classList.toggle("hidden", !(type === "all" || (e.dataset.types || "").includes(type))));
    };
    const syncNav = () => {
      let current = "";
      document.querySelectorAll(".log-entry:not(.hidden)").forEach((s) => { if (s.getBoundingClientRect().top <= 180) current = s.id; });
      document.querySelectorAll(".v-link").forEach((l) => l.classList.toggle("active", (l.getAttribute("href") || "").includes(current)));
    };
    (document.querySelector(".main-content") || window).addEventListener("scroll", syncNav, { passive: true });
    syncNav();
  }

  function initPrivacy() {
    if (!document.body.classList.contains("page-support-privacysupport")) return;
    window.toggleFaq = (el) => el?.closest(".faq-item")?.classList.toggle("active");
    $id("submitBtn")?.addEventListener("click", () => {
      const s = $id("ticketSubject"), m = $id("ticketMsg");
      if (!s?.value.trim() || !m?.value.trim()) return toast("Please fill subject and message.", "warning");
      s.value = ""; m.value = ""; toast("Support request submitted.", "success");
    });
    $id("exportDataBtn")?.addEventListener("click", () => toast("Preparing data export...", "info"));
    $id("deleteAccountBtn")?.addEventListener("click", () => window.confirm("Submit account deletion request?") && toast("Deletion request queued.", "warning"));
  }

  function initNotifications() {
    if (!document.body.classList.contains("page-support-notifications")) return;
    const subtitle = document.querySelector(".page-subtitle");
    const update = () => {
      const c = document.querySelectorAll(".noti-card.unread").length;
      if (subtitle) subtitle.innerHTML = c > 0 ? `You have <span style="color:var(--accent); font-weight:700;">${c}</span> unread notifications.` : "You have no unread notifications.";
    };
    const empty = () => {
      const visible = [...document.querySelectorAll(".noti-card")].some((c) => c.style.display !== "none");
      if ($id("emptyState")) $id("emptyState").style.display = visible ? "none" : "block";
      if ($id("notificationList")) $id("notificationList").style.display = visible ? "block" : "none";
    };
    window.readItem = (card) => { card?.classList?.remove("unread"); card?.setAttribute("data-state", "all"); update(); };
    window.markAllRead = () => document.querySelectorAll(".noti-card.unread").forEach((c) => window.readItem(c));
    window.archiveItem = (btn) => { const c = btn?.closest(".noti-card"); if (!c) return; c.dataset.archived = "true"; c.style.display = "none"; empty(); };
    window.filter = (type, tab) => {
      document.querySelectorAll(".tab").forEach((t) => t.classList.remove("active"));
      tab?.classList?.add("active");
      document.querySelectorAll(".noti-card").forEach((c) => {
        const unread = c.classList.contains("unread"), archived = c.dataset.archived === "true";
        const show = type === "all" ? !archived : type === "unread" ? unread && !archived : archived;
        c.style.display = show ? "flex" : "none";
      });
      empty();
    };
    document.querySelectorAll(".noti-card").forEach((c) => c.addEventListener("click", () => window.readItem(c)));
    $id("markAllBtn")?.addEventListener("click", () => window.markAllRead());
    document.querySelectorAll(".tab[data-filter]").forEach((t) => t.addEventListener("click", () => window.filter(t.dataset.filter || "all", t)));
    update(); empty();
  }

  function initProjects() {
    if (!document.body.classList.contains("page-projects-index")) return;
    const c = $id("projectsContainer"); if (!c) return;
    let f = "all", v = "grid";
    const apply = () => {
      const q = ($id("search")?.value || "").toLowerCase().trim();
      [...c.querySelectorAll(".project-card")].forEach((card) => {
        const ok = (!q || (card.dataset.name || "").includes(q)) && (f === "all" || (card.dataset.status || "") === f);
        card.style.display = ok ? "" : "none";
      });
      const mode = $id("sortSelect")?.value || "date";
      [...c.querySelectorAll(".project-card")].sort((a, b) => mode === "name"
        ? (a.dataset.name || "").localeCompare(b.dataset.name || "")
        : mode === "size"
          ? Number(b.dataset.size || 0) - Number(a.dataset.size || 0)
          : Number(b.dataset.date || 0) - Number(a.dataset.date || 0)).forEach((x) => c.appendChild(x));
    };
    const applyView = () => {
      c.classList.toggle("projects-grid", v === "grid");
      c.classList.toggle("projects-list", v === "list");
      document.querySelectorAll(".toggle-btn[data-view]").forEach((b) => b.classList.toggle("active", b.dataset.view === v));
    };
    $id("search")?.addEventListener("input", apply);
    $id("sortSelect")?.addEventListener("change", apply);
    document.querySelectorAll(".pill[data-filter]").forEach((p) => p.addEventListener("click", () => { f = p.dataset.filter || "all"; document.querySelectorAll(".pill[data-filter]").forEach((x) => x.classList.remove("active")); p.classList.add("active"); apply(); }));
    document.querySelectorAll(".toggle-btn[data-view]").forEach((b) => b.addEventListener("click", () => { v = b.dataset.view || "grid"; applyView(); }));
    applyView(); apply();
  }

  function initAnalytics() {
    if (!document.body.classList.contains("page-analytics-index")) return;
    // Analytics page now renders with server-sourced data and page-local scripts.
  }

  function initDeployLogs() {
    if (document.body.classList.contains("page-deploylogs-index")) {
      const b = $id("logsBody"), s = $id("searchBox"); if (b) {
        let f = "all";
        const apply = () => [...b.querySelectorAll("tr")].forEach((r) => r.style.display = ((f === "all" || (r.dataset.status || "") === f) && (!s?.value || (r.dataset.text || "").includes(s.value.toLowerCase()))) ? "" : "none");
        s?.addEventListener("input", apply);
        document.querySelectorAll(".filter-btn[data-filter]").forEach((x) => x.addEventListener("click", () => { f = x.dataset.filter || "all"; document.querySelectorAll(".filter-btn[data-filter]").forEach((k) => k.classList.remove("active")); x.classList.add("active"); apply(); }));
        document.querySelectorAll(".sync-btn").forEach((x) => x.addEventListener("click", () => { toast("Refreshing deploy list...", "info"); setTimeout(() => location.reload(), 250); }));
        apply();
      }
    }
    if (document.body.classList.contains("page-deploylogs-project")) {
      const pid = Number(new URLSearchParams(location.search).get("projectId")); if (!Number.isFinite(pid) || pid <= 0) return;
      const t = token(), reBtn = $id("reuploadBtn"), rsBtn = $id("restoreBtn"), inp = $id("reuploadInput");
      const busy = (btn, on, label) => { if (!btn) return; if (on) { btn.dataset.o = btn.innerHTML; btn.innerHTML = `<i class=\"fa-solid fa-spinner fa-spin\"></i> ${esc(label)}`; } else if (btn.dataset.o) btn.innerHTML = btn.dataset.o; btn.disabled = on; };
      reBtn?.addEventListener("click", () => inp?.click());
      inp?.addEventListener("change", async () => {
        if (!inp.files?.length) return;
        busy(reBtn, true, "Uploading...");
        try {
          const fd = new FormData(); if (t) fd.append("__RequestVerificationToken", t); fd.append("projectId", String(pid));
          [...inp.files].forEach((f) => fd.append("upload", f, f.webkitRelativePath || f.name));
          const { r, p } = await jsonReq(`/Dashboard/Index?handler=Reupload&projectId=${pid}`, { method: "POST", body: fd });
          if (!r.ok || !p?.success) throw new Error(msg(p, "Re-upload failed."));
          toast("Re-upload complete.", "success"); setTimeout(() => location.reload(), 650);
        } catch (e) { toast(e.message || "Re-upload failed.", "error"); } finally { busy(reBtn, false); inp.value = ""; }
      });
      rsBtn?.addEventListener("click", async () => {
        busy(rsBtn, true, "Restoring...");
        try {
          const { r, p } = await jsonReq(`/api/projects/${pid}/restore-last`, { method: "POST", headers: { "Content-Type": "application/json" }, body: "{}" });
          if (!r.ok || !p?.success) throw new Error(msg(p, "Restore failed."));
          toast(msg(p, "Backup restored."), "success"); setTimeout(() => location.reload(), 650);
        } catch (e) { toast(e.message || "Restore failed.", "error"); } finally { busy(rsBtn, false); }
      });
    }
  }
  function initChangeLog() {
    if (!document.body.classList.contains("page-support-changelog")) return;
    const logs = {
      "2": [
        { id: 101, user: "Administrator", avatar: "A", action: "Updated", target: "index.html", desc: "Modified hero section headline text.", time: "2 hours ago", type: "code", dateGroup: "Today", hasDiff: true, diff: [{ type: "context", line: 45, text: "<div class=\"hero-content\">" }, { type: "removed", line: 46, text: "  <h1>Welcome to the Future</h1>" }, { type: "added", line: 46, text: "  <h1>Build Faster with Bugence</h1>" }] },
        { id: 102, user: "Sarah", avatar: "S", action: "Added asset", target: "logo-white.svg", desc: "Uploaded logo file to /assets/images.", time: "5 hours ago", type: "assets", dateGroup: "Today", hasDiff: false }
      ],
      "3": [{ id: 201, user: "Administrator", avatar: "A", action: "Initial Commit", target: "Project", desc: "Created project and uploaded initial files.", time: "3 days ago", type: "code", dateGroup: "Previous", hasDiff: false }]
    };
    const ps = $id("projectSelect"), hf = $id("headerProjectName"), tf = $id("timelineFeed"), sq = $id("searchLog"), mf = $id("memberFilter"), ty = $id("typeFilter");
    if (!ps || !tf) return;
    window.toggleDiff = (id) => $id(`diff-${id}`)?.classList.toggle("open");
    const render = () => {
      const data = logs[ps.value] || logs["2"] || [];
      tf.innerHTML = "<div class='timeline-line'></div>";
      let g = "";
      data.forEach((l) => {
        if (l.dateGroup !== g) { g = l.dateGroup; tf.insertAdjacentHTML("beforeend", `<div class=\"date-separator\"><div class=\"date-badge\">${esc(g)}</div><div class=\"date-line\"></div></div>`); }
        tf.insertAdjacentHTML("beforeend", `<div class=\"activity-card type-${esc(l.type)}\" data-user=\"${esc(l.user)}\" data-type=\"${esc(l.type)}\"><div class=\"timeline-dot\"></div><div class=\"act-header\"><div class=\"user-meta\"><div class=\"avatar\">${esc(l.avatar)}</div><div><div class=\"act-title\">${esc(l.user)} <span style=\"margin:0 4px;\">-</span> ${esc(l.action)} <span style=\"color:var(--accent);\">${esc(l.target)}</span></div><div class=\"act-time\">${esc(l.time)}</div></div></div><div style=\"display:flex; gap:8px;\">${l.hasDiff ? `<button class=\"btn-action\" data-diff=\"${l.id}\"><i class=\"fa-solid fa-code\"></i> View Diff</button>` : ""}<button class=\"btn-action btn-revert\"><i class=\"fa-solid fa-rotate-left\"></i> Revert</button></div></div><div style=\"color:#ccc; font-size:.9rem; margin-left:48px;\">${esc(l.desc)}</div><div style=\"margin-left:48px;\">${l.hasDiff ? `<div class=\"act-details\" id=\"diff-${l.id}\">${(l.diff || []).map((d) => `<div class=\"diff-line ${esc(d.type)}\"><span class=\"line-num\">${esc(d.line)}</span> ${esc(d.text)}</div>`).join("")}</div>` : ""}</div></div>`);
      });
      filter();
    };
    const filter = () => {
      const q = (sq?.value || "").toLowerCase(), m = (mf?.value || "all").toLowerCase(), t = (ty?.value || "all").toLowerCase();
      document.querySelectorAll(".activity-card").forEach((c) => c.style.display = ((!q || c.textContent.toLowerCase().includes(q)) && (m === "all" || (c.dataset.user || "").toLowerCase().includes(m)) && (t === "all" || (c.dataset.type || "").toLowerCase() === t)) ? "block" : "none");
    };
    window.filterLog = filter;
    ps.addEventListener("change", () => { if (hf) hf.textContent = ps.options[ps.selectedIndex]?.text || "Project"; render(); });
    sq?.addEventListener("input", filter); mf?.addEventListener("change", filter); ty?.addEventListener("change", filter);
    tf.addEventListener("click", (e) => { const d = e.target.closest("[data-diff]"); if (d) return window.toggleDiff(Number(d.dataset.diff)); if (e.target.closest(".btn-revert")) toast("Rollback queued.", "info"); });
    $id("exportBtn")?.addEventListener("click", () => {
      const rows = (logs[ps.value] || []).map((l) => [l.user, l.action, l.target, l.type, l.time, l.desc]);
      const csv = ["User,Action,Target,Type,Time,Description", ...rows.map((r) => r.map((x) => `\"${String(x).replaceAll("\"", "\"\"")}\"`).join(","))].join("\n");
      const a = document.createElement("a"); a.href = URL.createObjectURL(new Blob([csv], { type: "text/csv;charset=utf-8;" })); a.download = "project-changelog.csv"; document.body.appendChild(a); a.click(); a.remove();
    });
    render();
  }

  function initDomains() {
    if (!document.body.classList.contains("page-settings-domains")) return;
    const ps = $id("projectSelect"), list = $id("domainList"), pBtn = $id("publishProjectBtn"), aBtn = $id("addDomainBtn"), input = $id("newDomainInput"), card = $id("domainContent"), t = token("#domainsAntiForgery");
    if (!ps || !list) return;
    let domains = [], cache = new Map(), preflight = new Map();
    const pid = () => Number(ps.value) || 0;
    const setPrimary = (d) => { if ($id("primaryDomain")) $id("primaryDomain").innerHTML = `${esc(d || "Not available")} <span class=\"badge badge-primary\">Primary</span>`; if ($id("visitLink")) { $id("visitLink").href = d ? `https://${d}` : "#"; $id("visitLink").classList.toggle("disabled", !d); } };
    const openIds = () => [...list.querySelectorAll("[data-domain-detail-row]")].filter((x) => x.style.display !== "none").map((x) => x.dataset.domainDetailRow).filter(Boolean);
    const row = (d) => `<tr data-domain-id="${esc(d.id)}"><td><div style="font-weight:700;margin-bottom:4px;">${esc(d.domain)}</div><div style="font-size:.85rem;color:var(--muted);"><span class="status-dot ${d.status === "Connected" && d.sslStatus === "Active" ? "dot-success" : d.failureCode ? "dot-error" : "dot-warning"}"></span>${esc(d.hostingStatus || d.status)}</div><div style="margin-top:4px;font-size:.78rem;color:#64748b;">${esc(d.dnsSatisfied)}/${esc(d.dnsRequired)} records detected</div></td><td><button class="btn-icon" data-domain-action="config" data-domain-id="${esc(d.id)}" style="width:auto;padding:0 12px;font-size:.8rem;">DNS Records</button></td><td><div>${d.sslStatus === "Active" ? '<i class="fa-solid fa-lock" style="color:var(--success)"></i> Active' : d.sslStatus === "Error" ? '<i class="fa-solid fa-triangle-exclamation" style="color:var(--danger)"></i> Error' : `<i class="fa-solid fa-spinner fa-spin" style="color:var(--warning)"></i> ${esc(d.sslStatus || "Provisioning")}`}</div>${d.failureCode || d.failureReason ? `<div style="margin-top:6px;font-size:.78rem;color:#fda4af;">${esc(d.failureCode || "ERROR")}${d.failureHint ? `<br>${esc(d.failureHint)}` : ""}</div>` : ""}</td><td style="text-align:right;white-space:nowrap;"><button class="btn-icon" data-domain-action="refresh" data-domain-id="${esc(d.id)}" title="Refresh"><i class="fa-solid fa-rotate-right"></i></button><button class="btn-icon" data-domain-action="preflight" data-domain-id="${esc(d.id)}" title="Preflight"><i class="fa-solid fa-wand-magic-sparkles"></i></button><button class="btn-icon delete" data-domain-action="delete" data-domain-id="${esc(d.id)}" title="Remove"><i class="fa-solid fa-trash"></i></button></td></tr><tr class="dns-detail-row" data-domain-detail-row="${esc(d.id)}" style="display:none;"><td colspan="4" style="padding:0;border:none;"><div class="config-details" id="config-${esc(d.id)}"></div></td></tr>`;
    const render = () => list.innerHTML = domains.length ? domains.map(row).join("") : `<tr><td colspan="4" style="text-align:center;color:var(--muted);">No custom domains added yet.</td></tr>`;
    const detail = (id) => {
      const c = $id(`config-${id}`), x = cache.get(String(id)) || { records: [], history: [] }, pf = preflight.get(String(id));
      if (!c) return;
      const rec = x.records.length ? x.records.map((r) => `<div class="copy-row"><div><div>${esc(r.type)} (${esc(r.purpose || "record")})</div><span class="dns-record">${esc(r.name)}</span></div><div style="display:flex;align-items:center;gap:10px;"><span class="dns-record">${esc(r.value)}</span>${r.satisfied ? '<span style="color:var(--success);">Detected</span>' : r.required ? '<span style="color:var(--warning);">Pending</span>' : '<span style="color:#64748b;">Optional</span>'}</div></div>`).join("") : `<div style="color:#64748b;font-size:.85rem;">No DNS instructions returned yet.</div>`;
      const his = x.history.length ? x.history.map((h) => `<div style="padding:8px 0;border-bottom:1px solid #1f2937;"><div style="font-size:.84rem;font-weight:700;color:#e5e7eb;">${esc(h.status)} / ${esc(h.sslStatus)}</div><div style="font-size:.82rem;color:#94a3b8;">${esc(h.message || "No detail message.")}</div><div style="font-size:.75rem;color:#64748b;">${esc(new Date(h.checkedAtUtc).toLocaleString())}</div></div>`).join("") : `<div style="color:#64748b;font-size:.85rem;">No verification history yet.</div>`;
      const pfHtml = pf?.checks?.length ? `<div style="margin-top:12px;padding-top:10px;border-top:1px solid #1f2937;"><div style="font-size:.82rem;font-weight:700;color:#cbd5e1;margin-bottom:8px;">Latest Preflight</div>${pf.checks.map((k) => `<div style="display:flex;gap:8px;margin-bottom:6px;"><i class="fa-solid ${k.ok ? "fa-circle-check" : "fa-circle-exclamation"}" style="margin-top:2px;color:${k.ok ? "var(--success)" : "var(--warning)"};"></i><div style="font-size:.82rem;"><div style="font-weight:700;color:#e2e8f0;">${esc(k.key)}</div><div style="color:#94a3b8;">${esc(k.detail || "")}</div></div></div>`).join("")}</div>` : "";
      c.innerHTML = `<div style="margin-bottom:12px;color:#f8fafc;font-weight:700;">DNS Instructions</div>${rec}<div style="margin-top:14px;color:#f8fafc;font-weight:700;">Verification Timeline</div>${his}${pfHtml}`;
    };
    const loadDetail = async (id, force = false) => {
      if (!force && cache.has(String(id))) return detail(id);
      const [dr, hr] = await Promise.all([jsonReq(`/api/domains/${id}/dns-records`), jsonReq(`/api/domains/${id}/history`)]);
      cache.set(String(id), { records: dr.r.ok ? (dr.p?.records || []) : [], history: hr.r.ok ? (hr.p?.history || []) : [] });
      detail(id);
    };
    const open = (id, on) => { const r = list.querySelector(`[data-domain-detail-row="${id}"]`), c = $id(`config-${id}`); if (!r || !c) return; r.style.display = on ? "" : "none"; c.classList.toggle("open", !!on); };
    const load = async (reopen = []) => {
      card?.classList.add("loading");
      try {
        const { r, p } = await jsonReq(`/api/projects/${pid()}/domains`);
        if (!r.ok) throw new Error(msg(p, "Unable to load domains."));
        domains = p?.domains || [];
        setPrimary(p?.project?.effectivePrimaryDomain || p?.project?.primaryDomain || ps.options[ps.selectedIndex]?.dataset.primaryDomain || "");
        render();
        reopen.forEach((id) => list.querySelector(`[data-domain-detail-row="${id}"]`) && (open(id, true), loadDetail(id).catch(() => {})));
      } catch (e) { list.innerHTML = `<tr><td colspan="4" style="text-align:center;color:#fca5a5;">${esc(e.message || "Unable to load domains.")}</td></tr>`; }
      finally { card?.classList.remove("loading"); }
    };
    const op = (url, method = "POST", body = "{}", headers = {}) => jsonReq(url, { method, headers: { ...headers, ...(t ? { RequestVerificationToken: t } : {}) }, body });
    list.addEventListener("click", async (e) => {
      const b = e.target.closest("[data-domain-action]"); if (!b) return;
      const a = b.dataset.domainAction, id = b.dataset.domainId; if (!a || !id) return;
      if (a === "config") { const is = list.querySelector(`[data-domain-detail-row="${id}"]`)?.style.display !== "none"; open(id, !is); if (!is) await loadDetail(id).catch(() => toast("Could not load DNS details.", "error")); return; }
      b.disabled = true; const o = openIds();
      try {
        if (a === "refresh") { const x = await op(`/api/domains/${id}/refresh`, "POST", "{}", { "Content-Type": "application/json" }); if (!x.r.ok) throw new Error(msg(x.p, "Refresh failed.")); toast("Domain refresh completed.", "success"); await load(o); }
        if (a === "preflight") { const x = await op(`/api/domains/${id}/preflight`, "POST", "{}", { "Content-Type": "application/json" }); if (!x.r.ok) throw new Error(msg(x.p, "Preflight failed.")); preflight.set(String(id), x.p || {}); const f = x.p?.checks?.find((k) => !k.ok && k.required); toast(f?.detail || "Preflight checks complete.", f ? "warning" : "success"); open(id, true); await loadDetail(id, true); }
        if (a === "delete") { if (!confirm("Remove this domain from the project?")) return; const x = await op(`/api/projects/${pid()}/domains/${id}`, "DELETE"); if (!x.r.ok && x.r.status !== 204) throw new Error(msg(x.p, "Failed to remove domain.")); cache.delete(String(id)); preflight.delete(String(id)); toast("Domain removed.", "success"); await load(); }
      } catch (er) { toast(er.message || "Domain operation failed.", "error"); } finally { b.disabled = false; }
    });
    const add = async () => {
      const d = input?.value?.trim() || "";
      if (!d) return toast("Enter a domain name first.", "warning");
      if (!/^[a-z0-9.-]+\.[a-z]{2,}$/i.test(d)) return toast("Enter a valid domain format.", "warning");
      if (aBtn) aBtn.disabled = true;
      try {
        const { r, p } = await jsonReq(`/api/projects/${pid()}/domains`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ domain: d }) });
        if (!r.ok) throw new Error(msg(p, "Unable to add domain."));
        if (input) input.value = "";
        toast("Domain added. Running verification...", "success");
        await load();
      } catch (e) { toast(e.message || "Unable to add domain.", "error"); }
      finally { if (aBtn) aBtn.disabled = false; }
    };
    aBtn?.addEventListener("click", add);
    input?.addEventListener("keydown", (e) => e.key === "Enter" && (e.preventDefault(), add()));
    pBtn?.addEventListener("click", async () => {
      if (pBtn) pBtn.disabled = true;
      try {
        const { r, p } = await jsonReq(`/api/projects/${pid()}/publish`, { method: "POST", headers: { "Content-Type": "application/json" }, body: "{}" });
        if (!r.ok) throw new Error(msg(p, "Publish failed."));
        toast(msg(p, "Project published."), "success");
        await load();
      } catch (e) { toast(e.message || "Publish failed.", "error"); }
      finally { if (pBtn) pBtn.disabled = false; }
    });
    ps.addEventListener("change", () => { cache.clear(); preflight.clear(); load(); });
    load();
  }
  function initTeams() {
    if (!document.body.classList.contains("page-settings-teams")) return;
    const t = token("#teamsAntiForgery"), inviteBtn = $id("inviteBtn"), inviteEmail = $id("inviteEmail"), inviteRole = $id("inviteRole"), status = $id("inviteStatus"), tbl = $id("membersTable");
    const set = (el, m, k) => { if (!el) return; el.textContent = m; el.style.color = k === "error" ? "#fca5a5" : k === "success" ? "#86efac" : k === "warning" ? "#fcd34d" : "#a1a1aa"; };
    const post = (h, b) => jsonReq(`/Settings/Teams?handler=${encodeURIComponent(h)}`, { method: "POST", headers: { "Content-Type": "application/json", ...(t ? { RequestVerificationToken: t } : {}) }, body: JSON.stringify(b || {}) });
    const invite = async (payload, host) => {
      const { r, p } = await post("Invite", payload);
      if (!r.ok || !p?.success) throw new Error(msg(p, "Unable to send invite."));
      set(host, msg(p, "Invite sent."), "success");
      if (p.warning) toast(p.warning, "warning");
      setTimeout(() => location.reload(), 550);
    };
    inviteBtn?.addEventListener("click", async () => {
      const em = inviteEmail?.value?.trim() || "", rl = inviteRole?.value || "Editor";
      if (!em) return set(status, "Please enter at least one email.", "warning");
      if (inviteBtn) inviteBtn.disabled = true;
      try { await invite({ emails: em, role: rl }, status); } catch (e) { set(status, e.message || "Unable to send invite.", "error"); } finally { if (inviteBtn) inviteBtn.disabled = false; }
    });
    const modal = $id("createLoginModal"), openModal = (x) => { if (modal) modal.style.display = x ? "flex" : "none"; };
    $id("createLoginBtn")?.addEventListener("click", () => openModal(true));
    $id("closeLoginModal")?.addEventListener("click", () => openModal(false));
    $id("cancelLoginCreate")?.addEventListener("click", () => openModal(false));
    modal?.addEventListener("click", (e) => e.target === modal && openModal(false));
    $id("saveLoginCreate")?.addEventListener("click", async () => {
      const fn = $id("loginFullName")?.value?.trim() || "", em = $id("loginEmail")?.value?.trim() || "", rl = $id("loginRole")?.value || "Editor", hs = $id("loginStatus");
      if (!em) return set(hs, "Email is required.", "warning");
      if ($id("saveLoginCreate")) $id("saveLoginCreate").disabled = true;
      try { await invite({ email: em, fullName: fn, role: rl }, hs); openModal(false); } catch (e) { set(hs, e.message || "Unable to create login invite.", "error"); } finally { if ($id("saveLoginCreate")) $id("saveLoginCreate").disabled = false; }
    });
    tbl?.addEventListener("click", async (e) => {
      const save = e.target.closest(".save-role[data-member-id]");
      if (save) {
        const id = save.dataset.memberId, role = tbl.querySelector(`[data-role-select=\"true\"][data-member-id=\"${id}\"]`)?.value || "";
        if (!id || !role) return;
        save.disabled = true;
        try {
          const { r, p } = await post("UpdateRole", { memberId: id, role });
          if (!r.ok || !p?.success) throw new Error(msg(p, "Unable to update role."));
          toast(msg(p, "Role updated."), "success"); setTimeout(() => location.reload(), 300);
        } catch (er) { toast(er.message || "Unable to update role.", "error"); }
        finally { save.disabled = false; }
        return;
      }
      const rm = e.target.closest(".delete[data-member-id]");
      if (rm) {
        const id = rm.dataset.memberId;
        if (!id || !confirm("Remove this member from the team?")) return;
        rm.disabled = true;
        try {
          const { r, p } = await post("RemoveMember", { memberId: id });
          if (!r.ok || !p?.success) throw new Error(msg(p, "Unable to remove member."));
          toast(msg(p, "Member removed."), "success"); setTimeout(() => location.reload(), 300);
        } catch (er) { toast(er.message || "Unable to remove member.", "error"); }
        finally { rm.disabled = false; }
        return;
      }
      const ri = e.target.closest(".delete[data-invite-id]");
      if (ri) {
        const id = ri.dataset.inviteId;
        if (!id || !confirm("Delete this pending invite?")) return;
        ri.disabled = true;
        try {
          const { r, p } = await post("RemoveInvite", { inviteId: id });
          if (!r.ok || !p?.success) throw new Error(msg(p, "Unable to delete invite."));
          toast(msg(p, "Invite deleted."), "success"); setTimeout(() => location.reload(), 300);
        } catch (er) { toast(er.message || "Unable to delete invite.", "error"); }
        finally { ri.disabled = false; }
      }
    });
  }

  function initDashboard() {
    if (!document.body.classList.contains("page-dashboard-index")) return;
    const t = token("#uploadForm"), setBtn = $id("settingsToggle"), menu = $id("settingsMenu"), ps = $id("projectSelect"), hn = $id("heroName"), hd = $id("heroDomain"), ho = $id("heroOverview"), hp = $id("heroDeploy");
    const settingsUploadBuildLink = $id("settingsUploadBuildLink");
    const settingsProjectSettingsLink = $id("settingsProjectSettingsLink");
    const settingsDeployLogsLink = $id("settingsDeployLogsLink");
    const filesBody = document.querySelector("#filesTable tbody");
    const filesSectionBadge = $id("filesSectionBadge");
    const navRoot = $id("navRoot");
    const navBack = $id("navBack");
    const currentPath = $id("currentPath");
    const fmtSize = (bytes) => {
      const b = Number(bytes) || 0;
      if (b < 1024) return `${b} B`;
      const kb = b / 1024;
      if (kb < 1024) return `${kb.toFixed(1).replace(/\.0$/, "")} KB`;
      const mb = kb / 1024;
      if (mb < 1024) return `${mb.toFixed(1).replace(/\.0$/, "")} MB`;
      const gb = mb / 1024;
      return `${gb.toFixed(2).replace(/\.00$/, "").replace(/(\.\d)0$/, "$1")} GB`;
    };
    const statusClass = (status) => (/fail|error/i.test(status || "") ? "st-danger" : /draft|pending/i.test(status || "") ? "st-warning" : "st-success");
    const safeProjectHub = (id) => `/ProjectHub/Index?projectId=${encodeURIComponent(String(id || ""))}`;
    const safeDeployLogsProject = (id) => `/DeployLogs/Project?projectId=${encodeURIComponent(String(id || ""))}`;
    const fileIcon = (name, isFolder) => {
      if (isFolder) return "<i class=\"fa-solid fa-folder-open file-icon file-icon-folder\"></i>";
      const ext = String(name || "").split(".").pop()?.toLowerCase() || "";
      if (["html", "htm"].includes(ext)) return "<i class=\"fa-solid fa-file-code file-icon file-icon-markup\"></i>";
      if (["css", "scss", "sass", "js", "mjs", "cjs", "ts", "tsx", "jsx", "json", "xml"].includes(ext)) return "<i class=\"fa-solid fa-file-code file-icon file-icon-code\"></i>";
      if (["png", "jpg", "jpeg", "gif", "svg", "webp", "avif", "bmp", "ico"].includes(ext)) return "<i class=\"fa-solid fa-file-image file-icon file-icon-image\"></i>";
      if (["zip", "rar", "7z", "tar", "gz"].includes(ext)) return "<i class=\"fa-solid fa-file-zipper file-icon file-icon-archive\"></i>";
      if (ext === "pdf") return "<i class=\"fa-solid fa-file-pdf file-icon file-icon-pdf\"></i>";
      return "<i class=\"fa-solid fa-file file-icon file-icon-generic\"></i>";
    };

    setBtn?.addEventListener("click", (e) => { e.preventDefault(); e.stopPropagation(); menu?.classList.toggle("show"); });
    document.addEventListener("click", (e) => { if (menu && setBtn && !menu.contains(e.target) && !setBtn.contains(e.target)) menu.classList.remove("show"); });

    const projectsFromRows = () => [...document.querySelectorAll("tr[data-project-row][data-project-id]")].map((r) => {
      const id = r.dataset.projectId || "";
      const name = r.querySelector("td:first-child div")?.textContent?.trim() || `Project ${id}`;
      const status = r.querySelector(".status-badge")?.textContent?.replace(/\s+/g, " ").trim() || "Uploaded";
      const uploaded = r.cells?.[3]?.textContent?.trim() || "";
      return {
        id,
        name,
        folder: name,
        size: 0,
        sizeDisplay: "0 B",
        uploaded,
        status,
        original: r.querySelector("td:first-child div:nth-child(2)")?.textContent?.trim() || "",
        preview: "#",
        domain: "",
        liveUrl: safeProjectHub(id),
        liveLabel: "Open project hub",
        liveType: "local-preview",
        domainConnected: false
      };
    });

    const normalizeProject = (item) => {
      const id = String(item?.id ?? "");
      const name = String(item?.name ?? item?.folder ?? `Project ${id}`);
      const folder = String(item?.folder ?? name);
      const size = Number(item?.size ?? 0);
      const preview = String(item?.preview ?? "#");
      const liveUrlRaw = typeof item?.liveUrl === "string" ? item.liveUrl : "";
      const liveUrl = liveUrlRaw || (preview && preview !== "#" ? preview : safeProjectHub(id));
      const domainConnected = !!item?.domainConnected;
      const liveLabel = String(item?.liveLabel ?? (domainConnected ? (item?.domain || name) : (preview && preview !== "#" ? "Open local preview" : "Open project hub")));
      return {
        id,
        name,
        folder,
        size,
        sizeDisplay: String(item?.sizeDisplay || fmtSize(size)),
        uploaded: String(item?.uploaded ?? ""),
        status: String(item?.status ?? "Uploaded"),
        original: String(item?.original ?? ""),
        preview,
        domain: String(item?.domain ?? ""),
        liveUrl,
        liveLabel,
        liveType: String(item?.liveType ?? (domainConnected ? "custom-domain" : "local-preview")),
        domainConnected
      };
    };

    let projects = [];
    let activeProjectId = null;
    let activeProjectName = "";
    let activePath = "";

    const setFilesBadge = (label) => { if (filesSectionBadge) filesSectionBadge.textContent = label; };
    const setCurrentPath = (label) => { if (currentPath) currentPath.textContent = label; };

    const renderRootTable = () => {
      activeProjectId = null;
      activeProjectName = "";
      activePath = "";
      if (!filesBody) return;
      setCurrentPath("/");
      setFilesBadge(`${projects.length} Project${projects.length === 1 ? "" : "s"}`);
      if (!projects.length) {
        filesBody.innerHTML = "<tr><td colspan=\"6\" class=\"text-center\" style=\"color:var(--muted);\">No uploads yet.</td></tr>";
        return;
      }
      filesBody.innerHTML = projects.map((p) => `
        <tr data-open-project-row="${esc(p.id)}">
          <td><button type="button" class="nav-btn" data-open-project="${esc(p.id)}" style="padding:4px 10px;">${fileIcon(p.name, true)}${esc(p.name)}</button></td>
          <td>Project</td>
          <td>${esc(p.sizeDisplay || fmtSize(p.size))}</td>
          <td>${esc(p.uploaded || "-")}</td>
          <td><span class="status-badge ${statusClass(p.status)}"><span class="dot"></span> ${esc(p.status || "Uploaded")}</span></td>
          <td class="actions-cell" style="text-align:right;"><button type="button" class="nav-btn" data-open-project="${esc(p.id)}">Open</button></td>
        </tr>`).join("");
    };

    const renderProjectChildren = (items) => {
      if (!filesBody) return;
      const sorted = [...(items || [])].sort((a, b) => {
        if (!!a.isFolder !== !!b.isFolder) return a.isFolder ? -1 : 1;
        return String(a.name || "").localeCompare(String(b.name || ""), undefined, { sensitivity: "base" });
      });
      setCurrentPath(`/${activeProjectName}${activePath ? `/${activePath}` : ""}`);
      setFilesBadge(`${sorted.length} Item${sorted.length === 1 ? "" : "s"}`);
      if (!sorted.length) {
        filesBody.innerHTML = "<tr><td colspan=\"6\" class=\"text-center\" style=\"color:var(--muted);\">No files in this folder.</td></tr>";
        return;
      }
      filesBody.innerHTML = sorted.map((item) => {
        const isFolder = !!item.isFolder;
        const name = String(item.name || "");
        const path = String(item.path || name);
        const type = isFolder ? "Folder" : (name.includes(".") ? name.split(".").pop().toUpperCase() : "File");
        return `
          <tr ${isFolder ? `data-open-folder-row="${esc(path)}"` : ""}>
            <td>${isFolder ? `<button type="button" class="nav-btn" data-open-folder="${esc(path)}" style="padding:4px 10px;">${fileIcon(name, true)}${esc(name)}</button>` : `${fileIcon(name, false)}${esc(name)}`}</td>
            <td>${esc(type)}</td>
            <td>${isFolder ? "-" : esc(fmtSize(item.size))}</td>
            <td>${esc((projects.find((p) => p.id === activeProjectId) || {}).uploaded || "-")}</td>
            <td><span class="status-badge ${statusClass((projects.find((p) => p.id === activeProjectId) || {}).status)}"><span class="dot"></span> ${esc((projects.find((p) => p.id === activeProjectId) || {}).status || "Uploaded")}</span></td>
            <td class="actions-cell" style="text-align:right;">${isFolder ? `<button type="button" class="nav-btn" data-open-folder="${esc(path)}">Open</button>` : "-"}</td>
          </tr>`;
      }).join("");
    };

    const loadProjectChildren = async () => {
      if (!activeProjectId) {
        renderRootTable();
        return;
      }
      const qp = activePath ? `&path=${encodeURIComponent(activePath)}` : "";
      const { r, p } = await jsonReq(`/Dashboard/Index?handler=Files&projectId=${encodeURIComponent(activeProjectId)}${qp}`);
      if (!r.ok) throw new Error(msg(p, "Unable to load project files."));
      renderProjectChildren(Array.isArray(p) ? p : []);
    };

    const openProjectRoot = async (projectId) => {
      const project = projects.find((x) => x.id === String(projectId));
      if (!project) return;
      activeProjectId = project.id;
      activeProjectName = project.name;
      activePath = "";
      await loadProjectChildren();
    };

    const openFolder = async (path) => {
      if (!activeProjectId) return;
      activePath = String(path || "").replace(/^\/+/, "").replace(/\\/g, "/");
      await loadProjectChildren();
    };

    const goBack = async () => {
      if (!activeProjectId) return;
      if (!activePath) {
        renderRootTable();
        return;
      }
      const parts = activePath.split("/").filter(Boolean);
      parts.pop();
      activePath = parts.join("/");
      await loadProjectChildren();
    };

    if (filesBody) {
      filesBody.addEventListener("click", (e) => {
        const target = e.target instanceof Element ? e.target : null;
        if (!target) return;
        const projectButton = target.closest("[data-open-project]");
        if (projectButton) {
          e.preventDefault();
          void openProjectRoot(projectButton.getAttribute("data-open-project"));
          return;
        }
        const folderButton = target.closest("[data-open-folder]");
        if (folderButton) {
          e.preventDefault();
          void openFolder(folderButton.getAttribute("data-open-folder"));
          return;
        }
        const projectRow = target.closest("tr[data-open-project-row]");
        if (projectRow) {
          e.preventDefault();
          void openProjectRoot(projectRow.getAttribute("data-open-project-row"));
          return;
        }
        const folderRow = target.closest("tr[data-open-folder-row]");
        if (folderRow) {
          e.preventDefault();
          void openFolder(folderRow.getAttribute("data-open-folder-row"));
        }
      });
    }

    navRoot?.addEventListener("click", (e) => { e.preventDefault(); renderRootTable(); });
    navBack?.addEventListener("click", (e) => { e.preventDefault(); void goBack(); });

    const hero = (p) => {
      if (!p) return;
      if (hn) hn.textContent = p.name;
      if (ho) ho.href = safeProjectHub(p.id);
      if (hd) {
        hd.textContent = p.liveLabel || "Open project hub";
        hd.href = p.liveUrl || safeProjectHub(p.id);
        hd.target = "_blank";
        hd.rel = "noopener noreferrer";
      }
      if (hp) hp.innerHTML = `<div><strong>${esc(p.status)}</strong></div><div style=\"color:#94a3b8;font-size:.86rem;\">${esc(p.date || "No recent timestamp")}</div>`;
    };

    const syncProjectActionLinks = (projectId) => {
      const hasId = projectId !== null && projectId !== undefined && String(projectId).trim().length > 0;
      if (settingsUploadBuildLink) settingsUploadBuildLink.href = hasId ? safeDeployLogsProject(projectId) : "/DeployLogs/Index";
      if (settingsDeployLogsLink) settingsDeployLogsLink.href = hasId ? safeDeployLogsProject(projectId) : "/DeployLogs/Index";
      if (settingsProjectSettingsLink) settingsProjectSettingsLink.href = hasId ? safeProjectHub(projectId) : "/ProjectHub/Index";
    };

    const syncProjectSelect = () => {
      if (!ps) return;
      ps.innerHTML = "";
      projects.forEach((p) => {
        const o = document.createElement("option");
        o.value = p.id;
        o.textContent = p.name;
        ps.appendChild(o);
      });
      const selected = projects.find((p) => p.id === ps.value) || projects[0];
      if (selected) {
        ps.value = selected.id;
        hero({
          ...selected,
          date: selected.uploaded
        });
        syncProjectActionLinks(selected.id);
      } else {
        syncProjectActionLinks(null);
      }
    };

    ps?.addEventListener("change", () => {
      const selected = projects.find((p) => p.id === ps.value) || projects[0];
      if (selected) {
        hero({
          ...selected,
          date: selected.uploaded
        });
        syncProjectActionLinks(selected.id);
      } else {
        syncProjectActionLinks(null);
      }
    });

    const hydrateProjects = async () => {
      const fallback = projectsFromRows().map(normalizeProject);
      try {
        const { r, p } = await jsonReq("/Dashboard/Index?handler=Projects");
        if (r.ok && Array.isArray(p) && p.length) {
          projects = p.map(normalizeProject);
        } else {
          projects = fallback;
        }
      } catch {
        projects = fallback;
      }
      syncProjectSelect();
      renderRootTable();
    };

    void hydrateProjects();

    const createDrawerOverlay = $id("projectCreateDrawerOverlay");
    const createDrawer = $id("projectCreateDrawer");
    const createDrawerClose = $id("projectCreateClose");
    const createDrawerCancel = $id("projectCreateCancel");
    const createDrawerSubmit = $id("projectCreateSubmit");
    const createDrawerError = $id("projectCreateError");
    const createName = $id("projectCreateName");
    const createDescription = $id("projectCreateDescription");
    const createRepo = $id("projectCreateRepo");
    const createDrop = $id("projectCreateDrop");
    const createUploadInput = $id("projectCreateUploadInput");
    const createZipInput = $id("projectCreateZipInput");
    const createUploadList = $id("projectCreateUploadList");
    const createSelectFolder = $id("projectCreateSelectFolder");
    const createSelectZip = $id("projectCreateSelectZip");
    const createStackInputs = () => [...document.querySelectorAll("[data-project-stack]")];
    let createUploadFiles = [];

    const setCreateError = (text) => {
      if (!createDrawerError) return;
      if (text) {
        createDrawerError.textContent = String(text);
        createDrawerError.style.display = "inline";
      } else {
        createDrawerError.textContent = "";
        createDrawerError.style.display = "none";
      }
    };

    const setCreateUploadList = () => {
      if (!createUploadList) return;
      if (!createUploadFiles.length) {
        createUploadList.textContent = "No files selected.";
        return;
      }
      const first = createUploadFiles.slice(0, 3).map((f) => f.webkitRelativePath || f.name);
      const extra = createUploadFiles.length - first.length;
      createUploadList.textContent = extra > 0
        ? `${first.join(", ")} (+${extra} more)`
        : first.join(", ");
    };

    const setCreateUploadFiles = (files) => {
      createUploadFiles = [...(files || [])].filter(Boolean);
      setCreateUploadList();
      setCreateError("");
    };

    const setDrawerBusy = (busy) => {
      if (!createDrawerSubmit) return;
      if (busy) {
        createDrawerSubmit.dataset.originalHtml = createDrawerSubmit.innerHTML;
        createDrawerSubmit.innerHTML = "<i class=\"fa-solid fa-spinner fa-spin\"></i> Creating...";
      } else if (createDrawerSubmit.dataset.originalHtml) {
        createDrawerSubmit.innerHTML = createDrawerSubmit.dataset.originalHtml;
      }
      createDrawerSubmit.disabled = busy;
      createDrawerCancel && (createDrawerCancel.disabled = busy);
      createDrawerClose && (createDrawerClose.disabled = busy);
    };

    const openCreateDrawer = () => {
      if (!createDrawer || !createDrawerOverlay) return;
      setCreateError("");
      createDrawer.classList.add("show");
      createDrawerOverlay.classList.add("show");
      createDrawer.setAttribute("aria-hidden", "false");
      document.body.classList.add("modal-open");
      if (createName && !createName.value.trim()) {
        createName.focus();
      }
    };

    const closeCreateDrawer = () => {
      if (!createDrawer || !createDrawerOverlay) return;
      createDrawer.classList.remove("show");
      createDrawerOverlay.classList.remove("show");
      createDrawer.setAttribute("aria-hidden", "true");
      document.body.classList.remove("modal-open");
      setCreateError("");
    };

    window.showProjectModal = openCreateDrawer;

    if (createDrawer && createDrawerOverlay) {
      createDrawerClose?.addEventListener("click", closeCreateDrawer);
      createDrawerCancel?.addEventListener("click", closeCreateDrawer);
      createDrawerOverlay.addEventListener("click", closeCreateDrawer);
      document.addEventListener("keydown", (e) => {
        if (e.key === "Escape" && createDrawer.classList.contains("show")) {
          closeCreateDrawer();
        }
      });

      createDrop?.addEventListener("click", () => createUploadInput?.click());
      createDrop?.addEventListener("dragover", (e) => {
        e.preventDefault();
        createDrop.classList.add("dragover");
      });
      createDrop?.addEventListener("dragleave", () => createDrop.classList.remove("dragover"));
      createDrop?.addEventListener("drop", (e) => {
        e.preventDefault();
        createDrop.classList.remove("dragover");
        const dropped = e.dataTransfer?.files;
        if (dropped?.length) {
          setCreateUploadFiles(dropped);
          if (createUploadInput) createUploadInput.value = "";
          if (createZipInput) createZipInput.value = "";
        }
      });

      createSelectFolder?.addEventListener("click", () => createUploadInput?.click());
      createSelectZip?.addEventListener("click", () => createZipInput?.click());

      createUploadInput?.addEventListener("change", () => {
        setCreateUploadFiles(createUploadInput.files);
        if (createZipInput) createZipInput.value = "";
      });

      createZipInput?.addEventListener("change", () => {
        setCreateUploadFiles(createZipInput.files);
        if (createUploadInput) createUploadInput.value = "";
      });

      createDrawerSubmit?.addEventListener("click", async () => {
        if (!createUploadFiles.length) {
          setCreateError("Select a folder or .zip/.rar before creating a project.");
          return;
        }

        setDrawerBusy(true);
        setCreateError("");
        try {
          const fd = new FormData();
          if (t) fd.append("__RequestVerificationToken", t);
          if (createName?.value?.trim()) fd.append("name", createName.value.trim());
          if (createDescription?.value?.trim()) fd.append("description", createDescription.value.trim());
          if (createRepo?.value?.trim()) fd.append("repoUrl", createRepo.value.trim());
          createStackInputs().forEach((input) => {
            if (input.checked) fd.append("stack", input.value || "");
          });
          createUploadFiles.forEach((f) => fd.append("upload", f, f.webkitRelativePath || f.name));

          const { r, p } = await jsonReq("/Dashboard/Index?handler=CreateProject", { method: "POST", body: fd });
          if (!r.ok || !p?.success) throw new Error(msg(p, "Unable to create project."));

          toast(msg(p, "Project created."), "success");
          closeCreateDrawer();
          setTimeout(() => location.reload(), 500);
        } catch (er) {
          setCreateError(er?.message || "Unable to create project.");
        } finally {
          setDrawerBusy(false);
        }
      });
    }

    const openProjectParam = new URLSearchParams(window.location.search).get("openProject");
    if (openProjectParam === "1" || openProjectParam === "true") {
      const next = new URL(window.location.href);
      next.searchParams.delete("openProject");
      history.replaceState({}, "", `${next.pathname}${next.search}`);
      openCreateDrawer();
    }

    const up = async (files, pid, h) => {
      if (!files?.length) return;
      const fd = new FormData();
      if (t) fd.append("__RequestVerificationToken", t);
      if (pid) fd.append("projectId", String(pid));
      [...files].forEach((f) => fd.append("upload", f, f.webkitRelativePath || f.name));
      const { r, p } = await jsonReq(`/Dashboard/Index?handler=${encodeURIComponent(h)}${pid ? `&projectId=${encodeURIComponent(String(pid))}` : ""}`, { method: "POST", body: fd });
      if (!r.ok || !p?.success) throw new Error(msg(p, "Upload failed."));
      toast(msg(p, "Upload complete."), "success");
      setTimeout(() => location.reload(), 650);
    };
    $id("uploadDrop")?.addEventListener("click", () => $id("uploadInput")?.click());
    $id("uploadDrop")?.addEventListener("dragover", (e) => { e.preventDefault(); $id("uploadDrop").classList.add("dragover"); });
    $id("uploadDrop")?.addEventListener("dragleave", () => $id("uploadDrop").classList.remove("dragover"));
    $id("uploadDrop")?.addEventListener("drop", (e) => { e.preventDefault(); $id("uploadDrop").classList.remove("dragover"); up(e.dataTransfer?.files, null, "Upload").catch((er) => toast(er.message || "Upload failed.", "error")); });
    $id("uploadInput")?.addEventListener("change", () => { up($id("uploadInput").files, null, "Upload").catch((er) => toast(er.message || "Upload failed.", "error")); $id("uploadInput").value = ""; });
    $id("uploadZipBtn")?.addEventListener("click", () => $id("uploadZipInput")?.click());
    $id("uploadZipInput")?.addEventListener("change", () => { up($id("uploadZipInput").files, null, "Upload").catch((er) => toast(er.message || "Upload failed.", "error")); $id("uploadZipInput").value = ""; });
    let rePid = null;
    $id("reuploadInputGlobal")?.addEventListener("change", () => {
      if (!rePid) return;
      up($id("reuploadInputGlobal").files, rePid, "Reupload").catch((er) => toast(er.message || "Re-upload failed.", "error"));
      rePid = null;
      $id("reuploadInputGlobal").value = "";
    });
    const post = (h, f) => jsonReq(`/Dashboard/Index?handler=${encodeURIComponent(h)}`, {
      method: "POST",
      headers: { "Content-Type": "application/x-www-form-urlencoded; charset=UTF-8" },
      body: new URLSearchParams({ ...(t ? { __RequestVerificationToken: t } : {}), ...Object.fromEntries(Object.entries(f || {}).map(([k, v]) => [k, String(v)])) }).toString()
    });
    document.addEventListener("click", async (e) => {
      const b = e.target.closest(".actions-btn");
      if (b) {
        e.preventDefault();
        e.stopPropagation();
        const m = b.nextElementSibling;
        document.querySelectorAll(".actions-menu").forEach((x) => x !== m && x.classList.remove("show"));
        m?.classList?.toggle("show");
        return;
      }
      const a = e.target.closest("[data-action]");
      if (a?.matches(".action-deploy,.action-rename,.action-delete")) {
        e.preventDefault();
        const id = Number(a.dataset.id), name = a.dataset.name || "Project", act = a.dataset.action;
        if (!Number.isFinite(id)) return;
        try {
          if (act === "deploy") {
            const { r, p } = await post("DeployLatest", { projectId: id });
            if (!r.ok || !p?.success) throw new Error(msg(p, "Deploy failed."));
            toast(`Deploy started for ${name}.`, "success");
            setTimeout(() => location.reload(), 650);
          }
          if (act === "reupload") { rePid = id; $id("reuploadInputGlobal")?.click(); }
          if (act === "rename") {
            const next = prompt("Rename project", name);
            if (!next?.trim()) return;
            const { r, p } = await post("RenameProject", { projectId: id, newName: next.trim() });
            if (!r.ok || !p?.success) throw new Error(msg(p, "Rename failed."));
            toast("Project renamed.", "success");
            setTimeout(() => location.reload(), 450);
          }
          if (act === "delete") {
            if (!confirm(`Delete ${name} permanently?`)) return;
            const { r, p } = await post("DeleteProject", { projectId: id });
            if (!r.ok || !p?.success) throw new Error(msg(p, "Delete failed."));
            toast("Project deleted.", "success");
            setTimeout(() => location.reload(), 450);
          }
        } catch (er) { toast(er.message || "Action failed.", "error"); }
      } else if (!e.target.closest(".actions-cell")) {
        document.querySelectorAll(".actions-menu").forEach((x) => x.classList.remove("show"));
      }
    });
  }

  const resolveElement = (ref, scope = document) => {
    if (!ref) return null;
    if (ref instanceof Element) return ref;
    if (typeof ref === "string") {
      const fromQuery = scope.querySelector(ref);
      if (fromQuery) return fromQuery;
      if (ref.startsWith("#")) return $id(ref.slice(1));
      return $id(ref);
    }
    return null;
  };

  const resolveElements = (ref, scope = document) => {
    if (!ref) return [];
    if (Array.isArray(ref)) return ref.flatMap((entry) => resolveElements(entry, scope));
    if (typeof ref === "string") return [...scope.querySelectorAll(ref)];
    if (ref instanceof Element) return [ref];
    if (typeof ref.length === "number") return [...ref].filter((x) => x instanceof Element);
    return [];
  };

  const parseJsonSafe = (raw, fallback = null) => {
    if (!raw) return fallback;
    try {
      return JSON.parse(raw);
    } catch {
      return fallback;
    }
  };

  const parseRowJson = (row) => parseJsonSafe(row?.getAttribute("data-row-json"), null);
  const toNumberOrNull = (value) => {
    const n = Number(value);
    return Number.isFinite(n) ? n : null;
  };
  const toIntOrNull = (value) => {
    const n = Number(value);
    return Number.isFinite(n) ? Math.trunc(n) : null;
  };
  const toBool = (value) => value === true || value === "true" || value === "True" || value === 1 || value === "1";
  const newDguid = () => `dg-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;

  function initClientPagination({
    tableBody,
    rowSelector = "tr",
    searchInput,
    paginationRoot,
    prevBtn,
    nextBtn,
    pageInfo,
    pageSize = 10
  }) {
    const tbody = resolveElement(tableBody);
    const search = resolveElement(searchInput);
    const pagination = resolveElement(paginationRoot);
    const prev = resolveElement(prevBtn);
    const next = resolveElement(nextBtn);
    const info = resolveElement(pageInfo);
    const state = { page: 1, pageSize: Math.max(1, Number(pageSize) || 10) };
    let generatedEmptyRow = null;

    if (!tbody) {
      return { refresh: () => { }, reset: () => { } };
    }

    const isEmptyRow = (row) => row?.dataset?.emptyRow === "1" || !!row?.querySelector("[data-empty-row='1']");
    const columnCount = () => {
      const table = tbody.closest("table");
      return table?.querySelectorAll("thead th").length || 1;
    };

    const removeGeneratedEmpty = () => {
      if (generatedEmptyRow?.parentElement) generatedEmptyRow.remove();
      generatedEmptyRow = null;
    };

    const showGeneratedEmpty = (text) => {
      removeGeneratedEmpty();
      const row = document.createElement("tr");
      row.dataset.generatedEmpty = "1";
      const col = document.createElement("td");
      col.colSpan = columnCount();
      col.className = "empty-state";
      col.textContent = text;
      row.appendChild(col);
      tbody.appendChild(row);
      generatedEmptyRow = row;
    };

    const apply = () => {
      const rows = [...tbody.querySelectorAll(rowSelector)];
      const staticEmptyRows = rows.filter((row) => isEmptyRow(row));
      const dataRows = rows.filter((row) => !isEmptyRow(row) && row.dataset.generatedEmpty !== "1");

      staticEmptyRows.forEach((row) => { row.style.display = "none"; });
      removeGeneratedEmpty();

      const term = (search?.value || "").trim().toLowerCase();
      const filtered = dataRows.filter((row) => {
        if (!term) return true;
        const hay = (row.dataset.search || row.textContent || "").toLowerCase();
        return hay.includes(term);
      });

      if (dataRows.length === 0) {
        if (staticEmptyRows.length) staticEmptyRows[0].style.display = "";
        else showGeneratedEmpty("No records yet.");
        if (pagination) pagination.style.display = "none";
        if (info) info.textContent = "Page 1 of 1";
        if (prev) prev.disabled = true;
        if (next) next.disabled = true;
        return;
      }

      if (!filtered.length) {
        dataRows.forEach((row) => { row.style.display = "none"; });
        showGeneratedEmpty("No matching records.");
        if (pagination) pagination.style.display = "none";
        if (info) info.textContent = "Page 1 of 1";
        if (prev) prev.disabled = true;
        if (next) next.disabled = true;
        return;
      }

      const totalPages = Math.max(1, Math.ceil(filtered.length / state.pageSize));
      if (state.page > totalPages) state.page = totalPages;
      if (state.page < 1) state.page = 1;
      const startIdx = (state.page - 1) * state.pageSize;
      const endIdx = startIdx + state.pageSize;

      dataRows.forEach((row) => { row.style.display = "none"; });
      filtered.forEach((row, idx) => {
        row.style.display = idx >= startIdx && idx < endIdx ? "" : "none";
      });

      if (pagination) pagination.style.display = filtered.length > state.pageSize ? "flex" : "none";
      if (prev) prev.disabled = state.page <= 1;
      if (next) next.disabled = state.page >= totalPages;
      if (info) info.textContent = `Page ${state.page} of ${totalPages}`;
    };

    search?.addEventListener("input", () => {
      state.page = 1;
      apply();
    });
    prev?.addEventListener("click", () => {
      if (state.page > 1) state.page -= 1;
      apply();
    });
    next?.addEventListener("click", () => {
      state.page += 1;
      apply();
    });

    apply();
    return {
      refresh: apply,
      reset: () => {
        state.page = 1;
        apply();
      }
    };
  }

  function wireDrawer({ openBtn, drawer, backdrop, closeBtns }) {
    const drawerEl = resolveElement(drawer);
    if (!drawerEl) return { open: () => { }, close: () => { }, isOpen: () => false };

    const openTargets = resolveElements(openBtn);
    const backdropEl = resolveElement(backdrop, drawerEl) || resolveElement(backdrop) || drawerEl.querySelector(".drawer-backdrop,.backdrop");
    const closeTargets = [
      ...resolveElements(closeBtns, drawerEl),
      ...resolveElements("[data-drawer-close]", drawerEl)
    ];

    const close = () => {
      drawerEl.classList.remove("show");
      drawerEl.setAttribute("aria-hidden", "true");
    };

    const open = () => {
      drawerEl.classList.add("show");
      drawerEl.setAttribute("aria-hidden", "false");
    };

    openTargets.forEach((target) => {
      target.addEventListener("click", (e) => {
        e.preventDefault();
        open();
      });
    });
    backdropEl?.addEventListener("click", close);
    closeTargets.forEach((target) => target.addEventListener("click", close));
    document.addEventListener("keydown", (e) => {
      if (e.key === "Escape" && drawerEl.classList.contains("show")) close();
    });

    return {
      open,
      close,
      isOpen: () => drawerEl.classList.contains("show")
    };
  }

  function wireConfirmModal({ modal, backdrop, closeBtns, confirmBtn }) {
    const modalEl = resolveElement(modal);
    if (!modalEl) return { open: () => { }, close: () => { }, setOnConfirm: () => { } };

    const titleEl = modalEl.querySelector("[data-confirm-title]");
    const messageEl = modalEl.querySelector("[data-confirm-message]");
    const submitEl = resolveElement(confirmBtn, modalEl) || modalEl.querySelector("[data-confirm-submit]");
    const backdropEl = resolveElement(backdrop, modalEl) || resolveElement(backdrop) || modalEl.querySelector(".confirm-backdrop");
    const closeEls = [
      ...resolveElements(closeBtns, modalEl),
      ...resolveElements("[data-confirm-close]", modalEl)
    ];
    let onConfirm = null;
    let busy = false;

    const close = () => {
      modalEl.classList.remove("show");
      modalEl.setAttribute("aria-hidden", "true");
    };

    const open = ({ title, message, confirmText, onConfirm: nextConfirm } = {}) => {
      if (titleEl && title) titleEl.textContent = title;
      if (messageEl && message) messageEl.textContent = message;
      if (submitEl && confirmText) submitEl.textContent = confirmText;
      onConfirm = typeof nextConfirm === "function" ? nextConfirm : null;
      modalEl.classList.add("show");
      modalEl.setAttribute("aria-hidden", "false");
    };

    closeEls.forEach((target) => target.addEventListener("click", close));
    backdropEl?.addEventListener("click", close);
    modalEl.addEventListener("click", (e) => {
      if (e.target === modalEl) close();
    });
    submitEl?.addEventListener("click", async () => {
      if (busy) return;
      busy = true;
      try {
        const result = onConfirm ? await onConfirm() : true;
        if (result !== false) close();
      } finally {
        busy = false;
      }
    });
    document.addEventListener("keydown", (e) => {
      if (e.key === "Escape" && modalEl.classList.contains("show")) close();
    });

    return {
      open,
      close,
      setOnConfirm: (cb) => { onConfirm = cb; }
    };
  }

  const postJsonHandler = async (handlerUrl, payload, antiforgeryScope) => {
    const headers = { "Content-Type": "application/json" };
    const t = token(antiforgeryScope) || token();
    if (t) {
      headers.RequestVerificationToken = t;
      headers["X-CSRF-TOKEN"] = t;
    }
    const { r, p } = await jsonReq(handlerUrl, {
      method: "POST",
      headers,
      body: JSON.stringify(payload ?? {})
    });
    if (!r.ok || p?.success === false) throw new Error(msg(p, "Request failed."));
    return p;
  };

  const refreshTableFromServer = async ({ tableBodyId, paginationRebind }) => {
    const current = $id(tableBodyId);
    if (!current) return false;

    const response = await fetch(location.href, { credentials: "same-origin", cache: "no-store" });
    if (!response.ok) throw new Error("Unable to refresh table.");
    const html = await response.text();
    const doc = new DOMParser().parseFromString(html, "text/html");
    const next = doc.getElementById(tableBodyId);
    if (!next) throw new Error("Refreshed table not found.");

    current.innerHTML = next.innerHTML;
    if (typeof paginationRebind === "function") paginationRebind();
    return true;
  };

  const fetchNextIdentity = async (basePath) => {
    const { r, p } = await jsonReq(`${basePath}?handler=NextIdentity`);
    if (!r.ok || p?.success === false) throw new Error(msg(p, "Unable to get identity."));
    return p || {};
  };

  function setupStandardCrud(config) {
    if (!document.body.classList.contains(config.bodyClass)) return null;
    const tableBody = $id(config.tableBodyId);
    if (!tableBody) return null;

    const pagination = initClientPagination({
      tableBody: tableBody,
      rowSelector: "tr",
      searchInput: $id(config.searchInputId),
      paginationRoot: $id(config.paginationId),
      prevBtn: $id("prevPage"),
      nextBtn: $id("nextPage"),
      pageInfo: $id("pageInfo"),
      pageSize: config.pageSize || 10
    });

    const createDrawer = wireDrawer({
      drawer: $id(config.createDrawerId),
      backdrop: `#${config.createDrawerId} .drawer-backdrop, #${config.createDrawerId} .backdrop`,
      closeBtns: `#${config.createDrawerId} [data-drawer-close]`
    });
    const editDrawer = wireDrawer({
      drawer: $id(config.editDrawerId),
      backdrop: `#${config.editDrawerId} .drawer-backdrop, #${config.editDrawerId} .backdrop`,
      closeBtns: `#${config.editDrawerId} [data-drawer-close]`
    });
    const confirmModal = wireConfirmModal({
      modal: $id(config.deleteModalId),
      closeBtns: `#${config.deleteModalId} [data-confirm-close]`,
      confirmBtn: `#${config.deleteModalId} [data-confirm-submit]`
    });

    const refreshRows = async () => {
      await refreshTableFromServer({
        tableBodyId: config.tableBodyId,
        paginationRebind: () => pagination.refresh()
      });
      if (typeof config.afterRefresh === "function") config.afterRefresh();
    };

    const openCreateBtn = resolveElement(config.openCreateSelector || "#openDrawer");
    openCreateBtn?.addEventListener("click", async (e) => {
      e.preventDefault();
      try {
        if (typeof config.prepareCreate === "function") await config.prepareCreate();
        createDrawer.open();
      } catch (er) {
        toast(er?.message || "Unable to open create form.", "error");
      }
    });

    const createSubmit = resolveElement(`#${config.createSubmitId}`);
    createSubmit?.addEventListener("click", async () => {
      try {
        const payload = config.getCreatePayload();
        const result = await postJsonHandler(`${config.basePath}?handler=${config.createHandler || "Create"}`, payload, config.antiforgeryScope);
        toast(msg(result, config.createSuccessMessage || "Record created."), "success");
        createDrawer.close();
        await refreshRows();
      } catch (er) {
        toast(er?.message || "Create failed.", "error");
      }
    });

    const editSubmit = resolveElement(`#${config.editSubmitId}`);
    editSubmit?.addEventListener("click", async () => {
      try {
        const payload = config.getEditPayload();
        const result = await postJsonHandler(`${config.basePath}?handler=${config.updateHandler || "Update"}`, payload, config.antiforgeryScope);
        toast(msg(result, config.updateSuccessMessage || "Record updated."), "success");
        editDrawer.close();
        await refreshRows();
      } catch (er) {
        toast(er?.message || "Update failed.", "error");
      }
    });

    tableBody.addEventListener("click", (e) => {
      const target = e.target instanceof Element ? e.target : null;
      if (!target) return;

      const editBtn = target.closest(config.editButtonSelector);
      if (editBtn) {
        e.preventDefault();
        const row = editBtn.closest("tr");
        const rowData = parseRowJson(row) || {};
        try {
          config.fillEdit(rowData, row, editBtn);
          editDrawer.open();
        } catch (er) {
          toast(er?.message || "Unable to load record.", "error");
        }
        return;
      }

      const deleteBtn = target.closest(config.deleteButtonSelector);
      if (deleteBtn) {
        e.preventDefault();
        const row = deleteBtn.closest("tr");
        const rowData = parseRowJson(row) || {};
        const deleteLabel = typeof config.getDeleteLabel === "function" ? config.getDeleteLabel(rowData, row, deleteBtn) : "this record";
        confirmModal.open({
          title: config.deleteTitle || "Delete Record?",
          message: `Delete ${deleteLabel}? This action cannot be undone.`,
          confirmText: config.deleteConfirmText || "Delete",
          onConfirm: async () => {
            const payload = config.getDeletePayload(rowData, row, deleteBtn);
            const result = await postJsonHandler(`${config.basePath}?handler=${config.deleteHandler || "Delete"}`, payload, config.antiforgeryScope);
            toast(msg(result, config.deleteSuccessMessage || "Record deleted."), "success");
            await refreshRows();
            return true;
          }
        });
        return;
      }

      if (typeof config.onTableClick === "function") {
        config.onTableClick(e, target, { refreshRows, pagination });
      }
    });

    if (typeof config.onInit === "function") {
      config.onInit({ refreshRows, pagination, tableBody, createDrawer, editDrawer, confirmModal });
    }

    return { refreshRows, pagination };
  }

  function initToolsDatabaseQuerySelector() {
    const basePath = "/Tools/DatabaseQuerySelector";
    setupStandardCrud({
      bodyClass: "page-tools-databasequeryselector",
      tableBodyId: "queryTableBody",
      searchInputId: "querySearch",
      paginationId: "queryPagination",
      basePath,
      createDrawerId: "queryCreateDrawer",
      editDrawerId: "queryEditDrawer",
      deleteModalId: "queryDeleteModal",
      createSubmitId: "queryCreateSubmit",
      editSubmitId: "queryEditSubmit",
      editButtonSelector: ".edit-query",
      deleteButtonSelector: ".delete-query",
      prepareCreate: async () => {
        const next = await fetchNextIdentity(basePath);
        $id("queryCreateId").textContent = String(next.nextId || "-");
        $id("queryCreateDguid").textContent = String(next.dguid || newDguid());
        if ($id("queryCreateName")) $id("queryCreateName").value = "";
        if ($id("queryCreateSqlText")) $id("queryCreateSqlText").value = "";
        if ($id("queryCreateFieldSql")) $id("queryCreateFieldSql").value = "";
      },
      getCreatePayload: () => {
        const name = ($id("queryCreateName")?.value || "").trim();
        const sqlText = ($id("queryCreateSqlText")?.value || "").trim();
        if (!name) throw new Error("Query name is required.");
        if (!sqlText) throw new Error("SQL is required.");
        return {
          id: toIntOrNull($id("queryCreateId")?.textContent) || 0,
          dguid: ($id("queryCreateDguid")?.textContent || "").trim(),
          name,
          sqlText,
          fieldGenerationSql: ($id("queryCreateFieldSql")?.value || "").trim()
        };
      },
      fillEdit: (rowData, row) => {
        const id = toIntOrNull(rowData?.id) || toIntOrNull(row?.dataset?.queryId);
        if (!id) throw new Error("Invalid record.");
        $id("queryEditId").textContent = String(id);
        $id("queryEditDguid").textContent = String(rowData?.dguid || "");
        if ($id("queryEditName")) $id("queryEditName").value = rowData?.name || "";
        if ($id("queryEditSqlText")) $id("queryEditSqlText").value = rowData?.sqlText || "";
        if ($id("queryEditFieldSql")) $id("queryEditFieldSql").value = rowData?.fieldGenerationSql || "";
      },
      getEditPayload: () => {
        const id = toIntOrNull($id("queryEditId")?.textContent);
        const name = ($id("queryEditName")?.value || "").trim();
        const sqlText = ($id("queryEditSqlText")?.value || "").trim();
        if (!id) throw new Error("Invalid record.");
        if (!name) throw new Error("Query name is required.");
        if (!sqlText) throw new Error("SQL is required.");
        return {
          id,
          dguid: ($id("queryEditDguid")?.textContent || "").trim(),
          name,
          sqlText,
          fieldGenerationSql: ($id("queryEditFieldSql")?.value || "").trim()
        };
      },
      getDeletePayload: (rowData, row) => ({ id: toIntOrNull(rowData?.id) || toIntOrNull(row?.dataset?.queryId) || 0 }),
      getDeleteLabel: (rowData) => rowData?.name || "this query"
    });
  }

  function initToolsTempleteViewer() {
    const basePath = "/Tools/TempleteViewer";
    setupStandardCrud({
      bodyClass: "page-tools-templeteviewer",
      tableBodyId: "viewerTableBody",
      searchInputId: "viewerSearch",
      paginationId: "viewerPagination",
      basePath,
      createDrawerId: "viewerCreateDrawer",
      editDrawerId: "viewerEditDrawer",
      deleteModalId: "viewerDeleteModal",
      createSubmitId: "viewerCreateSubmit",
      editSubmitId: "viewerEditSubmit",
      editButtonSelector: ".edit-viewer",
      deleteButtonSelector: ".delete-viewer",
      prepareCreate: async () => {
        const next = await fetchNextIdentity(basePath);
        $id("viewerCreateId").textContent = String(next.nextId || "-");
        $id("viewerCreateDguid").textContent = String(next.dguid || newDguid());
        if ($id("viewerCreateName")) $id("viewerCreateName").value = "";
        if ($id("viewerCreateType")) $id("viewerCreateType").value = "";
        if ($id("viewerCreateTemplate")) $id("viewerCreateTemplate").value = "";
      },
      getCreatePayload: () => {
        const name = ($id("viewerCreateName")?.value || "").trim();
        const viewerType = ($id("viewerCreateType")?.value || "").trim();
        const templateText = ($id("viewerCreateTemplate")?.value || "").trim();
        if (!name) throw new Error("Name is required.");
        if (!viewerType) throw new Error("Viewer type is required.");
        if (!templateText) throw new Error("Template text is required.");
        return {
          id: toIntOrNull($id("viewerCreateId")?.textContent) || 0,
          dguid: ($id("viewerCreateDguid")?.textContent || "").trim(),
          name,
          viewerType,
          templateText
        };
      },
      fillEdit: (rowData, row) => {
        const id = toIntOrNull(rowData?.id) || toIntOrNull(row?.dataset?.viewerId);
        if (!id) throw new Error("Invalid record.");
        $id("viewerEditId").textContent = String(id);
        $id("viewerEditDguid").textContent = String(rowData?.dguid || "");
        if ($id("viewerEditName")) $id("viewerEditName").value = rowData?.name || "";
        if ($id("viewerEditType")) $id("viewerEditType").value = rowData?.viewerType || "";
        if ($id("viewerEditTemplate")) $id("viewerEditTemplate").value = rowData?.templateText || "";
      },
      getEditPayload: () => {
        const id = toIntOrNull($id("viewerEditId")?.textContent);
        const name = ($id("viewerEditName")?.value || "").trim();
        const viewerType = ($id("viewerEditType")?.value || "").trim();
        const templateText = ($id("viewerEditTemplate")?.value || "").trim();
        if (!id) throw new Error("Invalid record.");
        if (!name) throw new Error("Name is required.");
        if (!viewerType) throw new Error("Viewer type is required.");
        if (!templateText) throw new Error("Template text is required.");
        return {
          id,
          dguid: ($id("viewerEditDguid")?.textContent || "").trim(),
          name,
          viewerType,
          templateText
        };
      },
      getDeletePayload: (rowData, row) => ({ id: toIntOrNull(rowData?.id) || toIntOrNull(row?.dataset?.viewerId) || 0 }),
      getDeleteLabel: (rowData) => rowData?.name || "this viewer"
    });
  }

  function initToolsMasterpage() {
    const basePath = "/Tools/Masterpage";
    setupStandardCrud({
      bodyClass: "page-tools-masterpage",
      tableBodyId: "masterTableBody",
      searchInputId: "masterSearch",
      paginationId: "masterPagination",
      basePath,
      createDrawerId: "masterCreateDrawer",
      editDrawerId: "masterEditDrawer",
      deleteModalId: "masterDeleteModal",
      createSubmitId: "masterCreateSubmit",
      editSubmitId: "masterEditSubmit",
      editButtonSelector: ".edit-master",
      deleteButtonSelector: ".delete-master",
      prepareCreate: async () => {
        const next = await fetchNextIdentity(basePath);
        $id("masterCreateId").textContent = String(next.nextId || "-");
        $id("masterCreateDguid").textContent = String(next.dguid || newDguid());
        if ($id("masterCreateName")) $id("masterCreateName").value = "";
        if ($id("masterCreateViewerId")) $id("masterCreateViewerId").value = "";
        if ($id("masterCreateText")) $id("masterCreateText").value = "";
      },
      getCreatePayload: () => {
        const name = ($id("masterCreateName")?.value || "").trim();
        const templateViewerId = toIntOrNull($id("masterCreateViewerId")?.value);
        const masterpageText = ($id("masterCreateText")?.value || "").trim();
        if (!name) throw new Error("Name is required.");
        if (!templateViewerId) throw new Error("Template viewer is required.");
        if (!masterpageText) throw new Error("Masterpage text is required.");
        return {
          id: toIntOrNull($id("masterCreateId")?.textContent) || 0,
          dguid: ($id("masterCreateDguid")?.textContent || "").trim(),
          name,
          templateViewerId,
          masterpageText
        };
      },
      fillEdit: (rowData, row) => {
        const id = toIntOrNull(rowData?.id) || toIntOrNull(row?.dataset?.masterId);
        if (!id) throw new Error("Invalid record.");
        $id("masterEditId").textContent = String(id);
        $id("masterEditDguid").textContent = String(rowData?.dguid || "");
        if ($id("masterEditName")) $id("masterEditName").value = rowData?.name || "";
        if ($id("masterEditViewerId")) $id("masterEditViewerId").value = String(toIntOrNull(rowData?.templateViewerId) || "");
        if ($id("masterEditText")) $id("masterEditText").value = rowData?.masterpageText || "";
      },
      getEditPayload: () => {
        const id = toIntOrNull($id("masterEditId")?.textContent);
        const name = ($id("masterEditName")?.value || "").trim();
        const templateViewerId = toIntOrNull($id("masterEditViewerId")?.value);
        const masterpageText = ($id("masterEditText")?.value || "").trim();
        if (!id) throw new Error("Invalid record.");
        if (!name) throw new Error("Name is required.");
        if (!templateViewerId) throw new Error("Template viewer is required.");
        if (!masterpageText) throw new Error("Masterpage text is required.");
        return {
          id,
          dguid: ($id("masterEditDguid")?.textContent || "").trim(),
          name,
          templateViewerId,
          masterpageText
        };
      },
      getDeletePayload: (rowData, row) => ({ id: toIntOrNull(rowData?.id) || toIntOrNull(row?.dataset?.masterId) || 0 }),
      getDeleteLabel: (rowData) => rowData?.name || "this masterpage"
    });
  }

  function initToolsPages() {
    const basePath = "/Tools/Pages";
    setupStandardCrud({
      bodyClass: "page-tools-pages",
      tableBodyId: "pagesTableBody",
      searchInputId: "pageSearch",
      paginationId: "pagePagination",
      basePath,
      createDrawerId: "pageCreateDrawer",
      editDrawerId: "pageEditDrawer",
      deleteModalId: "pageDeleteModal",
      createSubmitId: "pageCreateSubmit",
      editSubmitId: "pageEditSubmit",
      editButtonSelector: ".edit-page",
      deleteButtonSelector: ".delete-page",
      prepareCreate: async () => {
        const next = await fetchNextIdentity(basePath);
        $id("pageCreateId").textContent = String(next.nextId || "-");
        $id("pageCreateDguid").textContent = String(next.dguid || newDguid());
        if ($id("pageCreateName")) $id("pageCreateName").value = "";
        if ($id("pageCreateSlug")) $id("pageCreateSlug").value = "";
        if ($id("pageCreateMasterpageId")) $id("pageCreateMasterpageId").value = "";
      },
      getCreatePayload: () => {
        const name = ($id("pageCreateName")?.value || "").trim();
        if (!name) throw new Error("Name is required.");
        return {
          id: toIntOrNull($id("pageCreateId")?.textContent) || 0,
          dguid: ($id("pageCreateDguid")?.textContent || "").trim(),
          name,
          slug: ($id("pageCreateSlug")?.value || "").trim(),
          masterpageId: toIntOrNull($id("pageCreateMasterpageId")?.value)
        };
      },
      fillEdit: (rowData, row) => {
        const id = toIntOrNull(rowData?.id) || toIntOrNull(row?.dataset?.pageId);
        if (!id) throw new Error("Invalid record.");
        $id("pageEditId").textContent = String(id);
        $id("pageEditDguid").textContent = String(rowData?.dguid || "");
        if ($id("pageEditName")) $id("pageEditName").value = rowData?.name || "";
        if ($id("pageEditSlug")) $id("pageEditSlug").value = rowData?.slug || "";
        if ($id("pageEditMasterpageId")) $id("pageEditMasterpageId").value = String(toIntOrNull(rowData?.masterpageId) || "");
      },
      getEditPayload: () => {
        const id = toIntOrNull($id("pageEditId")?.textContent);
        const name = ($id("pageEditName")?.value || "").trim();
        if (!id) throw new Error("Invalid record.");
        if (!name) throw new Error("Name is required.");
        return {
          id,
          dguid: ($id("pageEditDguid")?.textContent || "").trim(),
          name,
          slug: ($id("pageEditSlug")?.value || "").trim(),
          masterpageId: toIntOrNull($id("pageEditMasterpageId")?.value)
        };
      },
      getDeletePayload: (rowData, row) => ({ id: toIntOrNull(rowData?.id) || toIntOrNull(row?.dataset?.pageId) || 0 }),
      getDeleteLabel: (rowData) => rowData?.name || "this page"
    });
  }

  function initToolsSystemProperties() {
    const basePath = "/Tools/SystemProperties";
    setupStandardCrud({
      bodyClass: "page-tools-systemproperties",
      tableBodyId: "propertiesTableBody",
      searchInputId: "propertySearch",
      paginationId: "propertyPagination",
      basePath,
      createDrawerId: "propertyCreateDrawer",
      editDrawerId: "propertyEditDrawer",
      deleteModalId: "propertyDeleteModal",
      createSubmitId: "propertyCreateSubmit",
      editSubmitId: "propertyEditSubmit",
      editButtonSelector: ".edit-property",
      deleteButtonSelector: ".delete-property",
      prepareCreate: async () => {
        const next = await fetchNextIdentity(basePath);
        $id("propertyCreateId").textContent = String(next.nextId || "-");
        $id("propertyCreateDguid").textContent = String(next.dguid || newDguid());
        ["Name", "Category", "Username", "Password", "Host", "Port", "RouteUrl", "Notes"].forEach((suffix) => {
          const el = $id(`propertyCreate${suffix}`);
          if (el) el.value = "";
        });
      },
      getCreatePayload: () => {
        const name = ($id("propertyCreateName")?.value || "").trim();
        const category = ($id("propertyCreateCategory")?.value || "").trim();
        if (!name) throw new Error("Name is required.");
        if (!category) throw new Error("Category is required.");
        return {
          id: toIntOrNull($id("propertyCreateId")?.textContent) || 0,
          dguid: ($id("propertyCreateDguid")?.textContent || "").trim(),
          name,
          category,
          username: ($id("propertyCreateUsername")?.value || "").trim(),
          password: ($id("propertyCreatePassword")?.value || "").trim(),
          host: ($id("propertyCreateHost")?.value || "").trim(),
          port: ($id("propertyCreatePort")?.value || "").trim(),
          routeUrl: ($id("propertyCreateRouteUrl")?.value || "").trim(),
          notes: ($id("propertyCreateNotes")?.value || "").trim()
        };
      },
      fillEdit: (rowData, row) => {
        const id = toIntOrNull(rowData?.id) || toIntOrNull(row?.dataset?.propertyId);
        if (!id) throw new Error("Invalid record.");
        $id("propertyEditId").textContent = String(id);
        $id("propertyEditDguid").textContent = String(rowData?.dguid || "");
        if ($id("propertyEditName")) $id("propertyEditName").value = rowData?.name || "";
        if ($id("propertyEditCategory")) $id("propertyEditCategory").value = rowData?.category || "";
        if ($id("propertyEditUsername")) $id("propertyEditUsername").value = rowData?.username || "";
        if ($id("propertyEditPassword")) $id("propertyEditPassword").value = rowData?.password || "";
        if ($id("propertyEditHost")) $id("propertyEditHost").value = rowData?.host || "";
        if ($id("propertyEditPort")) $id("propertyEditPort").value = rowData?.port || "";
        if ($id("propertyEditRouteUrl")) $id("propertyEditRouteUrl").value = rowData?.routeUrl || "";
        if ($id("propertyEditNotes")) $id("propertyEditNotes").value = rowData?.notes || "";
      },
      getEditPayload: () => {
        const id = toIntOrNull($id("propertyEditId")?.textContent);
        const name = ($id("propertyEditName")?.value || "").trim();
        const category = ($id("propertyEditCategory")?.value || "").trim();
        if (!id) throw new Error("Invalid record.");
        if (!name) throw new Error("Name is required.");
        if (!category) throw new Error("Category is required.");
        return {
          id,
          dguid: ($id("propertyEditDguid")?.textContent || "").trim(),
          name,
          category,
          username: ($id("propertyEditUsername")?.value || "").trim(),
          password: ($id("propertyEditPassword")?.value || "").trim(),
          host: ($id("propertyEditHost")?.value || "").trim(),
          port: ($id("propertyEditPort")?.value || "").trim(),
          routeUrl: ($id("propertyEditRouteUrl")?.value || "").trim(),
          notes: ($id("propertyEditNotes")?.value || "").trim()
        };
      },
      getDeletePayload: (rowData, row) => ({ id: toIntOrNull(rowData?.id) || toIntOrNull(row?.dataset?.propertyId) || 0 }),
      getDeleteLabel: (rowData) => rowData?.name || "this property"
    });
  }

  function initToolsPortlets() {
    const basePath = "/Tools/Portlets";
    setupStandardCrud({
      bodyClass: "page-tools-portlets",
      tableBodyId: "portletsTableBody",
      searchInputId: "portletSearch",
      paginationId: "portletPagination",
      basePath,
      createDrawerId: "portletCreateDrawer",
      editDrawerId: "portletEditDrawer",
      deleteModalId: "portletDeleteModal",
      createSubmitId: "portletCreateSubmit",
      editSubmitId: "portletEditSubmit",
      editButtonSelector: ".edit-portlet",
      deleteButtonSelector: ".delete-portlet",
      prepareCreate: async () => {
        const next = await fetchNextIdentity(basePath);
        $id("portletCreateId").textContent = String(next.nextId || "-");
        $id("portletCreateDguid").textContent = String(next.dguid || newDguid());
        if ($id("portletCreatePageId")) $id("portletCreatePageId").value = "";
        if ($id("portletCreateZoneKey")) $id("portletCreateZoneKey").value = "";
        if ($id("portletCreateTemplateViewerId")) $id("portletCreateTemplateViewerId").value = "";
        if ($id("portletCreateSortOrder")) $id("portletCreateSortOrder").value = "0";
      },
      getCreatePayload: () => {
        const pageId = toIntOrNull($id("portletCreatePageId")?.value);
        const templateViewerId = toIntOrNull($id("portletCreateTemplateViewerId")?.value);
        const zoneKey = ($id("portletCreateZoneKey")?.value || "").trim();
        if (!pageId) throw new Error("Page is required.");
        if (!templateViewerId) throw new Error("Template viewer is required.");
        if (!zoneKey) throw new Error("Zone key is required.");
        return {
          dguid: ($id("portletCreateDguid")?.textContent || "").trim(),
          pageId,
          zoneKey,
          templateViewerId,
          sortOrder: toIntOrNull($id("portletCreateSortOrder")?.value) || 0
        };
      },
      fillEdit: (rowData, row) => {
        const id = toIntOrNull(rowData?.id) || toIntOrNull(row?.dataset?.portletId);
        if (!id) throw new Error("Invalid record.");
        $id("portletEditId").textContent = String(id);
        $id("portletEditDguid").textContent = String(rowData?.dguid || "");
        if ($id("portletEditPageId")) $id("portletEditPageId").value = String(toIntOrNull(rowData?.pageId) || "");
        if ($id("portletEditZoneKey")) $id("portletEditZoneKey").value = rowData?.zoneKey || "";
        if ($id("portletEditTemplateViewerId")) $id("portletEditTemplateViewerId").value = String(toIntOrNull(rowData?.templateViewerId) || "");
        if ($id("portletEditSortOrder")) $id("portletEditSortOrder").value = String(toIntOrNull(rowData?.sortOrder) || 0);
      },
      getEditPayload: () => {
        const id = toIntOrNull($id("portletEditId")?.textContent);
        const pageId = toIntOrNull($id("portletEditPageId")?.value);
        const templateViewerId = toIntOrNull($id("portletEditTemplateViewerId")?.value);
        const zoneKey = ($id("portletEditZoneKey")?.value || "").trim();
        if (!id) throw new Error("Invalid record.");
        if (!pageId) throw new Error("Page is required.");
        if (!templateViewerId) throw new Error("Template viewer is required.");
        if (!zoneKey) throw new Error("Zone key is required.");
        return {
          id,
          pageId,
          zoneKey,
          templateViewerId,
          sortOrder: toIntOrNull($id("portletEditSortOrder")?.value) || 0
        };
      },
      getDeletePayload: (rowData, row) => ({ id: toIntOrNull(rowData?.id) || toIntOrNull(row?.dataset?.portletId) || 0 }),
      getDeleteLabel: (rowData) => rowData?.pageName ? `${rowData.pageName} portlet` : "this portlet"
    });
  }

  function initToolsPermissions() {
    const basePath = "/Tools/Permissions";
    setupStandardCrud({
      bodyClass: "page-tools-permissions",
      tableBodyId: "permissionsTableBody",
      searchInputId: "recordSearch",
      paginationId: "recordPagination",
      basePath,
      createDrawerId: "permissionCreateDrawer",
      editDrawerId: "permissionEditDrawer",
      deleteModalId: "permissionDeleteModal",
      createSubmitId: "permissionCreateSubmit",
      editSubmitId: "permissionEditSubmit",
      editButtonSelector: ".edit-record",
      deleteButtonSelector: ".delete-record",
      prepareCreate: async () => {
        const next = await fetchNextIdentity(basePath);
        $id("permissionCreateId").textContent = String(next.nextId || "-");
        $id("permissionCreateDguid").textContent = String(next.dguid || newDguid());
        if ($id("permissionCreateName")) $id("permissionCreateName").value = "";
        if ($id("permissionCreateSubjectType")) $id("permissionCreateSubjectType").value = "";
        if ($id("permissionCreateSubjectKey")) $id("permissionCreateSubjectKey").value = "";
        if ($id("permissionCreateAccessLevel")) $id("permissionCreateAccessLevel").value = "ViewOnly";
        if ($id("permissionCreateScope")) $id("permissionCreateScope").value = "";
        if ($id("permissionCreateEnabled")) $id("permissionCreateEnabled").checked = true;
        if ($id("permissionCreateNotes")) $id("permissionCreateNotes").value = "";
      },
      getCreatePayload: () => {
        const name = ($id("permissionCreateName")?.value || "").trim();
        const subjectType = ($id("permissionCreateSubjectType")?.value || "").trim();
        const subjectKey = ($id("permissionCreateSubjectKey")?.value || "").trim();
        const accessLevel = ($id("permissionCreateAccessLevel")?.value || "").trim();
        const scope = ($id("permissionCreateScope")?.value || "").trim();
        if (!name || !subjectType || !subjectKey || !accessLevel || !scope) {
          throw new Error("Name, subject type/key, access level, and scope are required.");
        }
        return {
          id: toIntOrNull($id("permissionCreateId")?.textContent) || 0,
          dguid: ($id("permissionCreateDguid")?.textContent || "").trim(),
          name,
          subjectType,
          subjectKey,
          accessLevel,
          scope,
          isEnabled: toBool($id("permissionCreateEnabled")?.checked),
          notes: ($id("permissionCreateNotes")?.value || "").trim()
        };
      },
      fillEdit: (rowData, row) => {
        const id = toIntOrNull(rowData?.id) || toIntOrNull(row?.dataset?.recordId);
        if (!id) throw new Error("Invalid record.");
        $id("permissionEditId").textContent = String(id);
        $id("permissionEditDguid").textContent = String(rowData?.dguid || "");
        if ($id("permissionEditName")) $id("permissionEditName").value = rowData?.name || "";
        if ($id("permissionEditSubjectType")) $id("permissionEditSubjectType").value = rowData?.subjectType || "";
        if ($id("permissionEditSubjectKey")) $id("permissionEditSubjectKey").value = rowData?.subjectKey || "";
        if ($id("permissionEditAccessLevel")) $id("permissionEditAccessLevel").value = rowData?.accessLevel || "";
        if ($id("permissionEditScope")) $id("permissionEditScope").value = rowData?.scope || "";
        if ($id("permissionEditEnabled")) $id("permissionEditEnabled").checked = toBool(rowData?.isEnabled);
        if ($id("permissionEditNotes")) $id("permissionEditNotes").value = rowData?.notes || "";
      },
      getEditPayload: () => {
        const id = toIntOrNull($id("permissionEditId")?.textContent);
        const name = ($id("permissionEditName")?.value || "").trim();
        const subjectType = ($id("permissionEditSubjectType")?.value || "").trim();
        const subjectKey = ($id("permissionEditSubjectKey")?.value || "").trim();
        const accessLevel = ($id("permissionEditAccessLevel")?.value || "").trim();
        const scope = ($id("permissionEditScope")?.value || "").trim();
        if (!id) throw new Error("Invalid record.");
        if (!name || !subjectType || !subjectKey || !accessLevel || !scope) {
          throw new Error("Name, subject type/key, access level, and scope are required.");
        }
        return {
          id,
          dguid: ($id("permissionEditDguid")?.textContent || "").trim(),
          name,
          subjectType,
          subjectKey,
          accessLevel,
          scope,
          isEnabled: toBool($id("permissionEditEnabled")?.checked),
          notes: ($id("permissionEditNotes")?.value || "").trim()
        };
      },
      getDeletePayload: (rowData, row) => ({ id: toIntOrNull(rowData?.id) || toIntOrNull(row?.dataset?.recordId) || 0 }),
      getDeleteLabel: (rowData) => rowData?.name || "this permission record"
    });
  }

  function initToolsReactBuilder() {
    const basePath = "/Tools/ReactBuilder";
    setupStandardCrud({
      bodyClass: "page-tools-reactbuilder",
      tableBodyId: "builderTableBody",
      searchInputId: "search",
      paginationId: "builderPagination",
      basePath,
      createDrawerId: "builderCreateDrawer",
      editDrawerId: "builderEditDrawer",
      deleteModalId: "builderDeleteModal",
      createSubmitId: "builderCreateSubmit",
      editSubmitId: "builderEditSubmit",
      editButtonSelector: ".icon-btn.edit",
      deleteButtonSelector: ".icon-btn.delete",
      prepareCreate: async () => {
        const next = await fetchNextIdentity(basePath);
        $id("builderCreateId").textContent = String(next.nextId || "-");
        $id("builderCreateDguid").textContent = String(next.dguid || newDguid());
        if ($id("builderCreateName")) $id("builderCreateName").value = "";
        if ($id("builderCreateDescription")) $id("builderCreateDescription").value = "";
        if ($id("builderCreateEntryFilePath")) $id("builderCreateEntryFilePath").value = "App.jsx";
        if ($id("builderCreatePreviewMasterpageId")) $id("builderCreatePreviewMasterpageId").value = "";
        if ($id("builderCreateIsActive")) $id("builderCreateIsActive").checked = true;
      },
      getCreatePayload: () => {
        const name = ($id("builderCreateName")?.value || "").trim();
        if (!name) throw new Error("Builder name is required.");
        return {
          id: toIntOrNull($id("builderCreateId")?.textContent) || 0,
          dguid: ($id("builderCreateDguid")?.textContent || "").trim(),
          name,
          description: ($id("builderCreateDescription")?.value || "").trim(),
          entryFilePath: ($id("builderCreateEntryFilePath")?.value || "").trim() || "App.jsx",
          previewMasterpageId: toIntOrNull($id("builderCreatePreviewMasterpageId")?.value),
          isActive: toBool($id("builderCreateIsActive")?.checked)
        };
      },
      fillEdit: (rowData, row) => {
        const id = toIntOrNull(rowData?.id) || toIntOrNull(row?.dataset?.id);
        if (!id) throw new Error("Invalid record.");
        $id("builderEditId").textContent = String(id);
        $id("builderEditDguid").textContent = String(rowData?.dguid || "");
        if ($id("builderEditName")) $id("builderEditName").value = rowData?.name || "";
        if ($id("builderEditDescription")) $id("builderEditDescription").value = rowData?.description || "";
        if ($id("builderEditEntryFilePath")) $id("builderEditEntryFilePath").value = rowData?.entryFilePath || "App.jsx";
        if ($id("builderEditPreviewMasterpageId")) $id("builderEditPreviewMasterpageId").value = String(toIntOrNull(rowData?.previewMasterpageId) || "");
        if ($id("builderEditIsActive")) $id("builderEditIsActive").checked = toBool(rowData?.isActive);
      },
      getEditPayload: () => {
        const id = toIntOrNull($id("builderEditId")?.textContent);
        const name = ($id("builderEditName")?.value || "").trim();
        if (!id) throw new Error("Invalid record.");
        if (!name) throw new Error("Builder name is required.");
        return {
          id,
          dguid: ($id("builderEditDguid")?.textContent || "").trim(),
          name,
          description: ($id("builderEditDescription")?.value || "").trim(),
          entryFilePath: ($id("builderEditEntryFilePath")?.value || "").trim() || "App.jsx",
          previewMasterpageId: toIntOrNull($id("builderEditPreviewMasterpageId")?.value),
          isActive: toBool($id("builderEditIsActive")?.checked)
        };
      },
      getDeletePayload: (rowData, row, btn) => ({ id: toIntOrNull(rowData?.id) || toIntOrNull(row?.dataset?.id) || toIntOrNull(btn?.dataset?.id) || 0 }),
      getDeleteLabel: (rowData) => rowData?.name || "this builder",
      onTableClick: (_e, target) => {
        const openEditorBtn = target.closest(".open-editor");
        if (!openEditorBtn) return;
        const id = toIntOrNull(openEditorBtn.dataset.id);
        if (id) window.location.href = `/Tools/ReactBuilderEditor?id=${encodeURIComponent(String(id))}`;
      }
    });
  }

  function initSupportDebugPanel() {
    if (!document.body.classList.contains("page-support-debugpanel")) return;
    initClientPagination({
      tableBody: $id("errorTableBody"),
      rowSelector: "tr",
      searchInput: $id("errorSearch"),
      paginationRoot: $id("errorPagination"),
      prevBtn: $id("prevPage"),
      nextBtn: $id("nextPage"),
      pageInfo: $id("pageInfo"),
      pageSize: 10
    });
  }

  function initToolsApplications() {
    if (!document.body.classList.contains("page-tools-applications")) return;
    const basePath = "/Tools/Applications";
    const tableBody = $id("applicationsTableBody");
    if (!tableBody) return;

    const pagination = initClientPagination({
      tableBody,
      rowSelector: "tr",
      searchInput: $id("tableSearch"),
      paginationRoot: $id("tablePagination"),
      prevBtn: $id("prevPage"),
      nextBtn: $id("nextPage"),
      pageInfo: $id("pageInfo"),
      pageSize: 10
    });

    const createDrawer = wireDrawer({
      drawer: $id("applicationsCreateDrawer"),
      backdrop: "#applicationsCreateDrawer .drawer-backdrop",
      closeBtns: "#applicationsCreateDrawer [data-drawer-close]"
    });
    const editDrawer = wireDrawer({
      drawer: $id("applicationsEditDrawer"),
      backdrop: "#applicationsEditDrawer .drawer-backdrop",
      closeBtns: "#applicationsEditDrawer [data-drawer-close]"
    });
    const permissionsDrawer = wireDrawer({
      drawer: $id("applicationsPermissionsDrawer"),
      backdrop: "#applicationsPermissionsDrawer .drawer-backdrop",
      closeBtns: "#applicationsPermissionsDrawer [data-drawer-close]"
    });
    const deleteModal = wireConfirmModal({
      modal: $id("applicationsDeleteModal"),
      closeBtns: "#applicationsDeleteModal [data-confirm-close]",
      confirmBtn: "#applicationsDeleteModal [data-confirm-submit]"
    });

    const refreshRows = async () => {
      await refreshTableFromServer({
        tableBodyId: "applicationsTableBody",
        paginationRebind: () => pagination.refresh()
      });
    };

    const typeOptions = ["int", "bigint", "nvarchar", "varchar", "char", "datetime", "decimal", "numeric", "float"];
    const createList = $id("appCreateColumnsList");
    const editList = $id("appEditColumnsList");
    const createName = $id("appCreateTableName");
    const editName = $id("appEditTableName");
    const editTableIdBox = $id("appEditTableId");
    let editingTableId = null;

    const normalizeLevel = (value) => {
      const v = String(value || "").replaceAll(" ", "").toLowerCase();
      if (v === "admin") return "Admin";
      if (v === "noaccess") return "NoAccess";
      return "ViewOnly";
    };

    const renderTypeOptions = (selected) => typeOptions.map((type) => `<option value="${type}" ${type === selected ? "selected" : ""}>${type}</option>`).join("");
    const rowNullable = (value) => value === undefined || value === null ? true : toBool(value);

    const buildColumnRow = (column = {}, isEdit = false) => {
      const row = document.createElement("div");
      row.className = "columns-grid";
      row.dataset.existing = isEdit && (column.isExisting !== false) ? "1" : "0";
      row.dataset.originalName = (column.originalName || column.name || "").trim();
      row.dataset.deleted = column.isDeleted ? "1" : "0";
      const selectedType = String(column.type || "nvarchar").toLowerCase();
      const nullable = rowNullable(column.isNullable);
      row.innerHTML = `
        <input class="form-input" data-col="name" value="${esc(column.name || "")}" placeholder="ColumnName" />
        <select class="form-input role-select" data-col="type">${renderTypeOptions(selectedType)}</select>
        <input class="form-input" data-col="length" type="number" min="1" value="${column.length ?? ""}" />
        <input class="form-input" data-col="precision" type="number" min="1" value="${column.precision ?? ""}" />
        <input class="form-input" data-col="scale" type="number" min="0" value="${column.scale ?? ""}" />
        <label style="display:flex;justify-content:center;align-items:center;"><input type="checkbox" data-col="nullable" ${nullable ? "checked" : ""} /></label>
        <button class="chip-btn ${isEdit ? "danger-btn" : ""}" type="button" data-col-action="${isEdit ? "toggle-delete" : "remove"}">${isEdit && column.isDeleted ? "Undo" : (isEdit ? "Delete" : "Remove")}</button>
      `;
      if (column.isDeleted) row.classList.add("column-deleted");
      return row;
    };

    const wireColumnList = (container) => {
      container?.addEventListener("click", (e) => {
        const target = e.target instanceof Element ? e.target : null;
        if (!target) return;
        const actionBtn = target.closest("[data-col-action]");
        if (!actionBtn) return;
        const row = actionBtn.closest(".columns-grid");
        if (!row) return;
        const action = actionBtn.getAttribute("data-col-action");
        if (action === "remove") {
          row.remove();
          return;
        }
        if (action === "toggle-delete") {
          const isDeleted = row.dataset.deleted === "1";
          row.dataset.deleted = isDeleted ? "0" : "1";
          row.classList.toggle("column-deleted", !isDeleted);
          actionBtn.textContent = isDeleted ? "Delete" : "Undo";
        }
      });
    };
    wireColumnList(createList);
    wireColumnList(editList);

    const addDefaultCreateColumn = () => {
      if (!createList) return;
      if (!createList.children.length) {
        createList.appendChild(buildColumnRow({ name: "Name", type: "nvarchar", length: 150, isNullable: false }, false));
      }
    };

    const collectColumns = (container) => {
      if (!container) return [];
      return [...container.querySelectorAll(".columns-grid")]
        .map((row) => {
          const nameInput = row.querySelector("[data-col='name']");
          const typeInput = row.querySelector("[data-col='type']");
          const lengthInput = row.querySelector("[data-col='length']");
          const precisionInput = row.querySelector("[data-col='precision']");
          const scaleInput = row.querySelector("[data-col='scale']");
          const nullableInput = row.querySelector("[data-col='nullable']");
          const isExisting = row.dataset.existing === "1";
          const originalName = (row.dataset.originalName || "").trim();
          const isDeleted = row.dataset.deleted === "1";
          const rawName = (nameInput?.value || "").trim();
          const name = rawName || originalName;
          if (!name) return null;
          if (!isExisting && isDeleted) return null;
          return {
            name,
            type: String(typeInput?.value || "nvarchar"),
            length: toIntOrNull(lengthInput?.value),
            precision: toIntOrNull(precisionInput?.value),
            scale: toIntOrNull(scaleInput?.value),
            isNullable: toBool(nullableInput?.checked),
            originalName: isExisting ? (originalName || name) : null,
            isDeleted,
            isExisting
          };
        })
        .filter(Boolean);
    };

    resolveElement("#appCreateAddColumn")?.addEventListener("click", () => {
      createList?.appendChild(buildColumnRow({ name: "", type: "nvarchar", isNullable: true }, false));
    });
    resolveElement("#appEditAddColumn")?.addEventListener("click", () => {
      editList?.appendChild(buildColumnRow({ name: "", type: "nvarchar", isNullable: true, isExisting: false }, false));
    });

    resolveElement("#openDrawer")?.addEventListener("click", (e) => {
      e.preventDefault();
      if (createName) createName.value = "";
      if (createList) createList.innerHTML = "";
      addDefaultCreateColumn();
      createDrawer.open();
    });

    resolveElement("#appCreateSubmit")?.addEventListener("click", async () => {
      try {
        const tableName = (createName?.value || "").trim();
        const columns = collectColumns(createList).map((column) => ({ ...column, isExisting: false, isDeleted: false, originalName: null }));
        if (!tableName) throw new Error("Table name is required.");
        if (!columns.length) throw new Error("Add at least one column.");
        const result = await postJsonHandler(`${basePath}?handler=Create`, { tableName, columns });
        toast(msg(result, "Application table created."), "success");
        createDrawer.close();
        await refreshRows();
      } catch (er) {
        toast(er?.message || "Unable to create table.", "error");
      }
    });

    resolveElement("#appEditSubmit")?.addEventListener("click", async () => {
      try {
        const tableName = (editName?.value || "").trim();
        const columns = collectColumns(editList);
        if (!editingTableId) throw new Error("Table context is missing.");
        if (!tableName) throw new Error("Table name is required.");
        if (!columns.length) throw new Error("Add at least one column.");
        const result = await postJsonHandler(`${basePath}?handler=Update`, { tableId: editingTableId, tableName, columns });
        toast(msg(result, "Application updated."), "success");
        editDrawer.close();
        await refreshRows();
      } catch (er) {
        toast(er?.message || "Unable to update table.", "error");
      }
    });

    let permissionState = {
      tableId: null,
      tableName: "",
      members: [],
      baseline: new Map(),
      current: new Map(),
      auditEntries: []
    };

    const permissionList = $id("appPermissionsList");
    const permissionSearch = $id("appPermissionsSearch");
    const permissionFilter = $id("appPermissionsFilter");
    const permissionMetrics = $id("appPermissionsMetrics");
    const permissionAuditList = $id("appPermissionsAuditList");
    const permissionEmpty = $id("appPermissionsEmpty");

    const renderPermissions = () => {
      if (!permissionList) return;
      const searchTerm = (permissionSearch?.value || "").trim().toLowerCase();
      const filterRole = (permissionFilter?.value || "all").toLowerCase();
      const rows = permissionState.members.filter((member) => {
        const role = String(member.role || "").toLowerCase();
        const hay = `${member.displayName || ""} ${member.email || ""} ${member.role || ""}`.toLowerCase();
        return (filterRole === "all" || role === filterRole) && (!searchTerm || hay.includes(searchTerm));
      });

      if (permissionEmpty) permissionEmpty.style.display = rows.length ? "none" : "flex";

      permissionList.innerHTML = rows.map((member) => {
        const userId = member.userId;
        const selected = normalizeLevel(member.locked ? "Admin" : (permissionState.current.get(userId) || member.explicitAccessLevel || member.effectiveAccessLevel));
        const baseline = normalizeLevel(member.explicitAccessLevel || member.effectiveAccessLevel);
        const changed = !member.locked && selected !== baseline;
        const initials = String(member.displayName || "U").split(" ").filter(Boolean).slice(0, 2).map((part) => part[0]?.toUpperCase() || "").join("") || "U";
        return `
          <div class="permission-row ${changed ? "changed" : ""}" data-perm-user="${esc(userId)}">
            <div class="permission-identity">
              <div class="permission-avatar">${esc(initials)}</div>
              <div style="min-width:0;">
                <div class="permission-name">${esc(member.displayName || "User")}</div>
                <div class="permission-email">${esc(member.email || "-")}</div>
              </div>
            </div>
            <div class="permission-role">${esc(member.role || "-")}</div>
            <div class="permission-control">
              ${member.locked ? `<div class="permission-lock"><i class="fa-solid fa-lock"></i> Owner access is locked</div>` : `
                <div class="permission-access">
                  <button type="button" class="access-btn ${selected === "Admin" ? "active admin" : ""}" data-perm-level="Admin" data-perm-user="${esc(userId)}">Admin</button>
                  <button type="button" class="access-btn ${selected === "ViewOnly" ? "active viewonly" : ""}" data-perm-level="ViewOnly" data-perm-user="${esc(userId)}">ViewOnly</button>
                  <button type="button" class="access-btn ${selected === "NoAccess" ? "active noaccess" : ""}" data-perm-level="NoAccess" data-perm-user="${esc(userId)}">NoAccess</button>
                </div>
              `}
            </div>
            <div class="permission-state">
              <div>${member.locked ? "Locked" : `Selected: ${esc(selected)}`}</div>
              <div class="${changed ? "override" : "default"}">${changed ? "Changed" : "No change"}</div>
            </div>
          </div>
        `;
      }).join("");

      const counts = { admin: 0, view: 0, noaccess: 0, changed: 0 };
      permissionState.members.forEach((member) => {
        const selected = normalizeLevel(member.locked ? "Admin" : (permissionState.current.get(member.userId) || member.explicitAccessLevel || member.effectiveAccessLevel));
        if (selected === "Admin") counts.admin += 1;
        if (selected === "ViewOnly") counts.view += 1;
        if (selected === "NoAccess") counts.noaccess += 1;
        if (!member.locked && selected !== normalizeLevel(member.explicitAccessLevel || member.effectiveAccessLevel)) counts.changed += 1;
      });
      if (permissionMetrics) {
        permissionMetrics.innerHTML = `
          <span class="metric admin">Admin ${counts.admin}</span>
          <span class="metric view">View ${counts.view}</span>
          <span class="metric noaccess">No Access ${counts.noaccess}</span>
          <span class="metric changed">Changed ${counts.changed}</span>
        `;
      }

      if (permissionAuditList) {
        if (!permissionState.auditEntries.length) {
          permissionAuditList.innerHTML = `<div class="permissions-audit-empty">No recent permission changes.</div>`;
        } else {
          permissionAuditList.innerHTML = permissionState.auditEntries.map((entry) => `
            <div class="permissions-audit-item">
              <div class="permissions-audit-main"><strong>${esc(entry.subjectDisplayName || "User")}</strong>: ${esc(entry.previousAccessLevel || "ViewOnly")} -> ${esc(entry.newAccessLevel || "ViewOnly")}</div>
              <div class="permissions-audit-meta">
                <span>By ${esc(entry.changedByDisplayName || "System")}</span>
                <span>${new Date(entry.changedAtUtc || Date.now()).toLocaleString()}</span>
              </div>
            </div>
          `).join("");
        }
      }
    };

    permissionList?.addEventListener("click", (e) => {
      const target = e.target instanceof Element ? e.target : null;
      const btn = target?.closest("[data-perm-level]");
      if (!btn) return;
      const userId = btn.getAttribute("data-perm-user");
      if (!userId) return;
      permissionState.current.set(userId, normalizeLevel(btn.getAttribute("data-perm-level")));
      renderPermissions();
    });
    permissionSearch?.addEventListener("input", renderPermissions);
    permissionFilter?.addEventListener("change", renderPermissions);
    resolveElement("#appPermissionsSetAllView")?.addEventListener("click", () => {
      permissionState.members.filter((member) => !member.locked).forEach((member) => permissionState.current.set(member.userId, "ViewOnly"));
      renderPermissions();
    });
    resolveElement("#appPermissionsSetAllNoAccess")?.addEventListener("click", () => {
      permissionState.members.filter((member) => !member.locked).forEach((member) => permissionState.current.set(member.userId, "NoAccess"));
      renderPermissions();
    });
    resolveElement("#appPermissionsReset")?.addEventListener("click", () => {
      permissionState.current = new Map(permissionState.baseline);
      renderPermissions();
    });

    resolveElement("#appPermissionsSave")?.addEventListener("click", async () => {
      try {
        if (!permissionState.tableId) throw new Error("No table selected.");
        const entries = permissionState.members
          .filter((member) => !member.locked)
          .map((member) => ({
            userId: member.userId,
            accessLevel: normalizeLevel(permissionState.current.get(member.userId) || member.explicitAccessLevel || member.effectiveAccessLevel)
          }));
        const result = await postJsonHandler(`${basePath}?handler=SavePermissions`, {
          tableId: permissionState.tableId,
          entries
        });
        toast(msg(result, "Permissions saved."), "success");
        permissionsDrawer.close();
        $id("permissionOnboardingBanner")?.remove();
        await refreshRows();
      } catch (er) {
        toast(er?.message || "Unable to save permissions.", "error");
      }
    });

    const openPermissions = async (tableId, tableName) => {
      const { r, p } = await jsonReq(`${basePath}?handler=Permissions&tableId=${encodeURIComponent(tableId)}`);
      if (!r.ok || p?.success === false) throw new Error(msg(p, "Unable to load permissions."));
      permissionState = {
        tableId: p.tableId || tableId,
        tableName: p.tableName || tableName || "Table",
        members: Array.isArray(p.members) ? p.members : [],
        baseline: new Map(),
        current: new Map(),
        auditEntries: Array.isArray(p.auditEntries) ? p.auditEntries : []
      };
      permissionState.members.forEach((member) => {
        const level = normalizeLevel(member.explicitAccessLevel || member.effectiveAccessLevel);
        permissionState.baseline.set(member.userId, level);
        permissionState.current.set(member.userId, level);
      });
      if ($id("appPermissionsTitle")) $id("appPermissionsTitle").textContent = `${permissionState.tableName} Permissions`;
      if ($id("appPermissionsSubtitle")) $id("appPermissionsSubtitle").textContent = `Manage explicit access entries for ${permissionState.tableName}.`;
      if (permissionSearch) permissionSearch.value = "";
      if (permissionFilter) permissionFilter.value = "all";
      renderPermissions();
      permissionsDrawer.open();
    };

    tableBody.addEventListener("click", async (e) => {
      const target = e.target instanceof Element ? e.target : null;
      if (!target) return;

      const syncBtn = target.closest(".sync-table");
      if (syncBtn) {
        e.preventDefault();
        const row = syncBtn.closest("tr");
        const tableId = row?.dataset?.tableId;
        if (!tableId) return;
        try {
          const result = await postJsonHandler(`${basePath}?handler=SyncColumns`, { tableId });
          toast(msg(result, "Columns synced."), "success");
          await refreshRows();
        } catch (er) {
          toast(er?.message || "Unable to sync columns.", "error");
        }
        return;
      }

      const editBtn = target.closest(".edit-table");
      if (editBtn) {
        e.preventDefault();
        const row = editBtn.closest("tr");
        const rowData = parseRowJson(row) || {};
        editingTableId = rowData.tableId || row?.dataset?.tableId;
        const tableName = rowData.displayName || rowData.tableName || row?.dataset?.tableName || "";
        const columns = Array.isArray(rowData.columns) ? rowData.columns : parseJsonSafe(row?.getAttribute("data-columns"), []);
        if (!editingTableId) return;
        if (editTableIdBox) editTableIdBox.textContent = String(editingTableId);
        if (editName) editName.value = tableName;
        if (editList) {
          editList.innerHTML = "";
          (columns || []).forEach((column) => {
            editList.appendChild(buildColumnRow({
              name: column.name || column.Name || "",
              type: (column.type || column.Type || "nvarchar").toLowerCase(),
              length: column.length ?? column.Length ?? "",
              precision: column.precision ?? column.Precision ?? "",
              scale: column.scale ?? column.Scale ?? "",
              isNullable: column.isNullable ?? column.IsNullable ?? true,
              isExisting: true,
              originalName: column.name || column.Name || "",
              isDeleted: false
            }, true));
          });
          if (!editList.children.length) editList.appendChild(buildColumnRow({ name: "", type: "nvarchar", isNullable: true, isExisting: false }, false));
        }
        editDrawer.open();
        return;
      }

      const deleteBtn = target.closest(".delete-table");
      if (deleteBtn) {
        e.preventDefault();
        const row = deleteBtn.closest("tr");
        const rowData = parseRowJson(row) || {};
        const tableId = rowData.tableId || row?.dataset?.tableId;
        const tableName = rowData.displayName || row?.dataset?.tableName || "this table";
        if (!tableId) return;
        deleteModal.open({
          title: "Delete Application Table?",
          message: `Delete ${tableName}? This action drops all records in the table.`,
          confirmText: "Delete",
          onConfirm: async () => {
            const result = await postJsonHandler(`${basePath}?handler=Delete`, { tableId });
            toast(msg(result, "Application deleted."), "success");
            await refreshRows();
            return true;
          }
        });
        return;
      }

      const permissionsBtn = target.closest(".permissions-table");
      if (permissionsBtn) {
        e.preventDefault();
        const row = permissionsBtn.closest("tr");
        const tableId = permissionsBtn.getAttribute("data-table-id") || row?.dataset?.tableId;
        const tableName = permissionsBtn.getAttribute("data-table-name") || row?.dataset?.tableName || "Table";
        if (!tableId) return;
        try {
          await openPermissions(tableId, tableName);
        } catch (er) {
          toast(er?.message || "Unable to open permissions.", "error");
        }
      }
    });

    resolveElement("#openFirstPermissionSetup")?.addEventListener("click", (e) => {
      e.preventDefault();
      const firstPermissionsButton = tableBody.querySelector(".permissions-table");
      if (!firstPermissionsButton) {
        toast("No tables available for permission setup.", "warning");
        return;
      }
      firstPermissionsButton.dispatchEvent(new MouseEvent("click", { bubbles: true }));
    });
  }

  const start = () => {
    initDashboard();
    initGlobalSettings();
    initUpdates();
    initPrivacy();
    initNotifications();
    initProjects();
    initAnalytics();
    initDeployLogs();
    initChangeLog();
    initDomains();
    initTeams();
    initToolsApplications();
    initToolsDatabaseQuerySelector();
    initToolsTempleteViewer();
    initToolsMasterpage();
    initToolsPages();
    initToolsSystemProperties();
    initToolsPortlets();
    initToolsPermissions();
    initToolsReactBuilder();
    initSupportDebugPanel();
  };

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", start, { once: true });
  } else {
    start();
  }
})();
