(function(){
  if (window.BugencePopupSidebar) return;

  const STYLE_ID = 'bugence-popup-sidebar-kit-style';

  function ensureStyle(doc){
    if (doc.getElementById(STYLE_ID)) return;
    const style = doc.createElement('style');
    style.id = STYLE_ID;
    style.textContent = `
      .bps-root { height:100%; display:flex; flex-direction:column; color:#e9eef8; font-family:'Satoshi',system-ui,-apple-system,sans-serif; }
      .bps-head { padding:12px 12px 10px; border-bottom:1px solid rgba(255,255,255,.09); background:linear-gradient(180deg, rgba(255,255,255,.03), rgba(255,255,255,.01)); }
      .bps-title { margin:0; font-size:.88rem; font-weight:700; letter-spacing:.03em; color:#f6f8ff; }
      .bps-tabs { display:flex; flex-wrap:wrap; gap:6px; margin-top:10px; }
      .bps-tab { border:1px solid rgba(255,255,255,.16); background:rgba(255,255,255,.03); color:#cfd9ea; border-radius:999px; padding:5px 11px; font-size:.72rem; font-weight:700; cursor:pointer; }
      .bps-tab:hover { color:#fff; border-color:rgba(255,255,255,.35); }
      .bps-tab.is-active { color:#eaffff; border-color:rgba(45,212,191,.68); background:rgba(45,212,191,.16); box-shadow:inset 0 0 0 1px rgba(45,212,191,.16); }
      .bps-search-wrap { padding:10px 12px; border-bottom:1px solid rgba(255,255,255,.08); }
      .bps-search { width:100%; background:#0b0f17; border:1px solid rgba(255,255,255,.14); color:#ebf1fc; border-radius:10px; padding:8px 10px; font-size:.79rem; }
      .bps-search:focus { outline:none; border-color:rgba(56,189,248,.72); box-shadow:0 0 0 3px rgba(56,189,248,.12); }
      .bps-body { flex:1; min-height:0; overflow:auto; padding:10px; }
      .bps-body::-webkit-scrollbar { width:8px; }
      .bps-body::-webkit-scrollbar-thumb { background:rgba(255,255,255,.14); border-radius:999px; }
      .bps-empty { font-size:.77rem; color:#90a0ba; padding:10px 6px; }
      .bps-group { border:1px solid rgba(255,255,255,.11); border-radius:12px; background:#10131b; overflow:hidden; margin-bottom:10px; }
      .bps-group-btn { width:100%; border:none; background:rgba(255,255,255,.02); color:#edf4ff; text-align:left; padding:10px 11px; cursor:pointer; display:flex; justify-content:space-between; align-items:center; gap:10px; }
      .bps-group-title { font-size:.79rem; font-weight:700; }
      .bps-group-sub { font-size:.68rem; color:#95a4be; margin-top:2px; }
      .bps-chevron { font-size:.73rem; color:#94a8c5; transform:rotate(0deg); transition:transform .16s ease; }
      .bps-group.is-open .bps-chevron { transform:rotate(180deg); }
      .bps-items { display:none; border-top:1px solid rgba(255,255,255,.08); background:#0d1118; padding:9px; }
      .bps-group.is-open .bps-items { display:block; }
      .bps-item { width:100%; text-align:left; border:1px solid rgba(255,255,255,.13); background:rgba(255,255,255,.03); color:#e7edf8; padding:8px 10px; border-radius:9px; font-size:.78rem; margin-bottom:7px; cursor:pointer; }
      .bps-item:hover { border-color:rgba(56,189,248,.7); background:rgba(56,189,248,.12); color:#fff; }
    `;
    doc.head.appendChild(style);
  }

  function byId(doc, id){ return doc.getElementById(id); }

  function init(config){
    const doc = config?.doc || document;
    const mount = byId(doc, config?.mountId || 'popupSidebarMount');
    if (!mount) return null;

    ensureStyle(doc);

    const tabs = Array.isArray(config?.tabs) ? config.tabs : [];
    const groupsByTab = config?.groupsByTab || {};
    const onInsert = typeof config?.onInsert === 'function' ? config.onInsert : function(){};
    const options = Object.assign({ defaultCollapsed:true, accordionMode:'single', searchable:true }, config?.options || {});
    const nonCollapsibleTabs = new Set(Array.isArray(options.nonCollapsibleTabs) ? options.nonCollapsibleTabs : []);
    const flatTabs = new Set(Array.isArray(options.flatTabs) ? options.flatTabs : []);

    let activeTabId = options.defaultCollapsed ? null : (tabs[0]?.id || null);
    let openGroupId = null;
    let searchTerm = '';

    mount.innerHTML = '';
    const root = doc.createElement('div'); root.className = 'bps-root';
    const head = doc.createElement('div'); head.className = 'bps-head';
    const title = doc.createElement('h3'); title.className = 'bps-title'; title.textContent = config?.title || 'Controls';
    const tabRow = doc.createElement('div'); tabRow.className = 'bps-tabs';
    const searchWrap = doc.createElement('div'); searchWrap.className = 'bps-search-wrap';
    const search = doc.createElement('input'); search.className = 'bps-search'; search.placeholder = 'Filter...';
    const body = doc.createElement('div'); body.className = 'bps-body';

    head.appendChild(title);
    head.appendChild(tabRow);
    if (options.searchable) { searchWrap.appendChild(search); root.appendChild(head); root.appendChild(searchWrap); }
    else { root.appendChild(head); }
    root.appendChild(body);
    mount.appendChild(root);

    const makeTab = (tab) => {
      const btn = doc.createElement('button');
      btn.type = 'button';
      btn.className = 'bps-tab';
      btn.textContent = tab.label;
      btn.setAttribute('aria-pressed', 'false');
      btn.addEventListener('click', () => {
        if (activeTabId === tab.id) {
          if (nonCollapsibleTabs.has(tab.id)) {
            activeTabId = tab.id;
          } else {
            activeTabId = null;
            openGroupId = null;
          }
        } else {
          activeTabId = tab.id;
          openGroupId = null;
        }
        render();
      });
      btn.addEventListener('keydown', (ev) => {
        if (ev.key === 'Enter' || ev.key === ' ') { ev.preventDefault(); btn.click(); }
      });
      return btn;
    };

    const tabButtons = tabs.map((tab) => ({ tab, btn: makeTab(tab) }));
    tabButtons.forEach((x) => tabRow.appendChild(x.btn));

    if (options.searchable) {
      search.addEventListener('input', () => {
        searchTerm = (search.value || '').trim().toLowerCase();
        render();
      });
    }

    doc.addEventListener('keydown', (ev) => {
      if (ev.key === 'Escape' && activeTabId) {
        if (nonCollapsibleTabs.has(activeTabId)) return;
        activeTabId = null;
        openGroupId = null;
        render();
      }
    });

    function render(){
      tabButtons.forEach(({ tab, btn }) => {
        const active = activeTabId === tab.id;
        btn.classList.toggle('is-active', active);
        btn.setAttribute('aria-pressed', active ? 'true' : 'false');
      });

      body.innerHTML = '';
      if (!activeTabId) {
        const empty = doc.createElement('div'); empty.className = 'bps-empty'; empty.textContent = 'Select a tab to expand tokens.';
        body.appendChild(empty);
        return;
      }

      const groups = Array.isArray(groupsByTab[activeTabId]) ? groupsByTab[activeTabId] : [];
      const isFlatTab = flatTabs.has(activeTabId);
      let anyVisible = false;

      groups.forEach((group) => {
        const items = Array.isArray(group.items) ? group.items : [];
        const filteredItems = !searchTerm
          ? items
          : items.filter((item) => {
              const hay = `${item?.label || ''} ${item?.token || ''} ${group?.title || ''} ${group?.subtitle || ''}`.toLowerCase();
              return hay.includes(searchTerm);
            });

        if (!filteredItems.length) return;
        anyVisible = true;

        const section = doc.createElement('section'); section.className = 'bps-group';
        const header = doc.createElement('button');
        header.type = 'button';
        header.className = 'bps-group-btn';

        const left = doc.createElement('div');
        const hTitle = doc.createElement('div'); hTitle.className = 'bps-group-title'; hTitle.textContent = group.title || 'Group';
        const hSub = doc.createElement('div'); hSub.className = 'bps-group-sub'; hSub.textContent = group.subtitle || '';
        left.appendChild(hTitle); if (hSub.textContent) left.appendChild(hSub);

        const chev = doc.createElement('span'); chev.className = 'bps-chevron'; chev.textContent = '?';
        header.appendChild(left); header.appendChild(chev);

        const itemWrap = doc.createElement('div'); itemWrap.className = 'bps-items';
        filteredItems.forEach((item) => {
          const btn = doc.createElement('button');
          btn.type = 'button';
          btn.className = 'bps-item';
          btn.textContent = item.label || item.token || 'Token';
          btn.addEventListener('click', () => onInsert(item));
          itemWrap.appendChild(btn);
        });

        const groupId = group.id || `${activeTabId}-${Math.random().toString(36).slice(2)}`;
        const isOpen = isFlatTab ? true : (openGroupId === groupId);
        section.classList.toggle('is-open', isOpen);
        header.setAttribute('aria-expanded', isOpen ? 'true' : 'false');
        if (isFlatTab) {
          header.style.cursor = 'default';
          const chevEl = header.querySelector('.bps-chevron');
          if (chevEl) chevEl.style.visibility = 'hidden';
        } else {
          header.addEventListener('click', () => {
            if (options.accordionMode === 'single') {
              openGroupId = (openGroupId === groupId) ? null : groupId;
            } else {
              openGroupId = (openGroupId === groupId) ? null : groupId;
            }
            render();
          });
        }

        section.appendChild(header);
        section.appendChild(itemWrap);
        body.appendChild(section);
      });

      if (!anyVisible) {
        const empty = doc.createElement('div'); empty.className = 'bps-empty'; empty.textContent = searchTerm ? 'No results for this tab.' : 'No entries available for this tab.';
        body.appendChild(empty);
      }
    }

    render();
    return {
      setActiveTab: (tabId) => { activeTabId = tabId || null; openGroupId = null; render(); },
      collapse: () => { activeTabId = null; openGroupId = null; render(); },
      refresh: () => render()
    };
  }

  window.BugencePopupSidebar = { init };
})();
