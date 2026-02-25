(function () {
  if (window.__bugenceConsoleMobileSidebarInit) return;
  window.__bugenceConsoleMobileSidebarInit = true;

  var SIDEBAR_STATE_KEY = 'bugence.sidebar.collapsed';

  function isMobile() {
    return window.matchMedia('(max-width: 1024px)').matches;
  }

  function inUnifiedScope() {
    var path = (window.location.pathname || '').toLowerCase();
    return path.indexOf('/tools/') === 0 ||
      path.indexOf('/workflows/') === 0 ||
      path.indexOf('/dashboard/') === 0 ||
      path.indexOf('/projects/') === 0 ||
      path.indexOf('/analytics/') === 0 ||
      path.indexOf('/projecthub/') === 0 ||
      path.indexOf('/editor/') === 0 ||
      path.indexOf('/settings/teams') === 0 ||
      path.indexOf('/settings/domains') === 0 ||
      path.indexOf('/settings/application') === 0 ||
      path.indexOf('/settings/globalsettings') === 0 ||
      path.indexOf('/settings/profile') === 0 ||
      path.indexOf('/settings/editprofile') === 0 ||
      path.indexOf('/settings/resetpassword') === 0 ||
      path.indexOf('/settings/billing') === 0 ||
      path.indexOf('/support/') === 0 ||
      path.indexOf('/deploylogs/') === 0 ||
      path.indexOf('/application/table') === 0 ||
      path.indexOf('/application/record') === 0 ||
      path.indexOf('/content/editbyslug') === 0 ||
      path.indexOf('/content/edit') === 0;
  }

  function pickSidebar() {
    var allAsides = Array.prototype.slice.call(document.querySelectorAll('aside'));
    var scored = allAsides
      .map(function (aside) {
        var navCount = aside.querySelectorAll('.nav-item').length;
        var hasBrand = /BUGENCE/i.test(aside.textContent || '');
        var score = navCount + (hasBrand ? 5 : 0);
        return { aside: aside, score: score, navCount: navCount };
      })
      .filter(function (x) { return x.navCount >= 6; })
      .sort(function (a, b) { return b.score - a.score; });

    return scored.length ? scored[0].aside : null;
  }

  function ensureMenuSection(sidebar, labelCandidates) {
    var labels = Array.prototype.slice.call(sidebar.querySelectorAll('.menu-cat, .menu-label, .nav-section-title'));
    var found = labels.find(function (node) {
      var text = (node.textContent || '').trim().toLowerCase();
      return labelCandidates.some(function (candidate) { return text === candidate; });
    });
    if (found) return found;

    var label = document.createElement('div');
    label.className = 'menu-cat';
    label.textContent = labelCandidates[0].charAt(0).toUpperCase() + labelCandidates[0].slice(1);
    var profileArea = sidebar.querySelector('.profile-block, .profile-wrapper');
    if (profileArea && profileArea.parentElement === sidebar) {
      sidebar.insertBefore(label, profileArea);
    } else {
      sidebar.appendChild(label);
    }
    return label;
  }

  function insertLinkAfterSection(sidebar, sectionNode, link) {
    var container = sectionNode && sectionNode.parentElement ? sectionNode.parentElement : sidebar;
    if (!container) return;
    var next = sectionNode.nextElementSibling;
    var lastInSection = sectionNode;
    while (next) {
      if (next.matches('.menu-cat, .menu-label, .nav-section-title, .profile-block, .profile-wrapper')) break;
      lastInSection = next;
      next = next.nextElementSibling;
    }
    if (lastInSection && lastInSection.parentElement !== container) {
      container.appendChild(link);
      return;
    }
    if (lastInSection && lastInSection.nextSibling) {
      container.insertBefore(link, lastInSection.nextSibling);
    } else {
      container.appendChild(link);
    }
  }

  function ensureNavLink(sidebar, sectionNode, href, iconClass, text) {
    var exists = Array.prototype.slice.call(sidebar.querySelectorAll('a.nav-item')).some(function (a) {
      var current = (a.getAttribute('href') || '').toLowerCase();
      return current === href.toLowerCase();
    });
    if (exists) return;

    var a = document.createElement('a');
    a.className = 'nav-item';
    a.setAttribute('href', href);
    a.innerHTML = '<i class="' + iconClass + ' nav-icon"></i> ' + text;
    insertLinkAfterSection(sidebar, sectionNode, a);
  }

  function normalizeUnifiedMenu(sidebar) {
    if (!inUnifiedScope()) return;
    var tools = ensureMenuSection(sidebar, ['tools']);
    var config = ensureMenuSection(sidebar, ['configuration']);
    var support = ensureMenuSection(sidebar, ['support']);

    ensureNavLink(sidebar, tools, '/Tools/Applications', 'fa-solid fa-table', 'Applications');
    ensureNavLink(sidebar, tools, '/Tools/DatabaseQuerySelector', 'fa-solid fa-database', 'Database Query Selector');
    ensureNavLink(sidebar, tools, '/Tools/TempleteViewer', 'fa-solid fa-layer-group', 'Templete Viewer');
    ensureNavLink(sidebar, tools, '/Tools/Masterpage', 'fa-solid fa-window-restore', 'Masterpage');
    ensureNavLink(sidebar, tools, '/Tools/Pages', 'fa-solid fa-file-lines', 'Pages');
    ensureNavLink(sidebar, tools, '/Tools/ReactBuilder', 'fa-brands fa-react', 'React Builder');
    ensureNavLink(sidebar, tools, '/Tools/SystemProperties', 'fa-solid fa-sliders', 'System Properties');
    ensureNavLink(sidebar, tools, '/Tools/Permissions', 'fa-solid fa-shield-halved', 'Permissions');
    ensureNavLink(sidebar, tools, '/Tools/Portlets', 'fa-solid fa-cubes', 'Portlets');
    ensureNavLink(sidebar, tools, '/Workflows/Index', 'fa-solid fa-diagram-project', 'Workflow');
    ensureNavLink(sidebar, tools, '/Content/EditBySlug?slug=index', 'fa-solid fa-pen-to-square', 'Text Editor');

    ensureNavLink(sidebar, config, '/Settings/Teams', 'fa-solid fa-users', 'Team');
    ensureNavLink(sidebar, config, '/Settings/Domains', 'fa-solid fa-globe', 'Domains');
    ensureNavLink(sidebar, config, '/Settings/Application', 'fa-solid fa-gear', 'Settings');

    ensureNavLink(sidebar, support, '/Support/ChangeLog', 'fa-solid fa-clock-rotate-left', 'Change Log');
    ensureNavLink(sidebar, support, '/DeployLogs/Index', 'fa-solid fa-list-check', 'Deploy Logs');
    ensureNavLink(sidebar, support, '/Support/DebugPanel', 'fa-solid fa-bug', 'Debug Panel');
    ensureNavLink(sidebar, support, '/Support/PrivacySupport', 'fa-solid fa-shield-halved', 'Privacy Support');
    ensureNavLink(sidebar, support, '/Support/UpdatesAnnouncements', 'fa-solid fa-bullhorn', 'Updates & Announcements');
  }

  function markActiveNav(sidebar) {
    var path = (window.location.pathname || '').toLowerCase();
    var query = (window.location.search || '').toLowerCase();
    Array.prototype.slice.call(sidebar.querySelectorAll('a.nav-item')).forEach(function (a) {
      var href = (a.getAttribute('href') || '').toLowerCase();
      if (!href) return;
      var isActive = href.indexOf('?') >= 0
        ? (path + query) === href
        : (path === href || path.indexOf(href + '/') === 0);
      if (href === '/tools/reactbuilder' && path === '/tools/reactbuildereditor') {
        isActive = true;
      }
      a.classList.toggle('active', isActive);
    });
  }

  function ensureDesktopCollapseToggle(sidebar) {
    if (document.getElementById('consoleSidebarCollapseBtn')) return;
    var host = sidebar.querySelector('.sidebar-head') || sidebar.querySelector('.brand') || sidebar;
    var btn = document.createElement('button');
    btn.type = 'button';
    btn.id = 'consoleSidebarCollapseBtn';
    btn.className = 'console-sidebar-collapse-btn';
    btn.setAttribute('aria-label', 'Toggle sidebar');
    btn.innerHTML = '<i class="fa-solid fa-chevron-left"></i>';
    host.appendChild(btn);
  }

  function ensureCollapsedHeader(sidebar) {
    if (!sidebar) return;
    if (sidebar.querySelector('.console-collapsed-stack')) return;
    var host = sidebar.querySelector('.sidebar-head') || sidebar.querySelector('.ph-topbar') || sidebar;
    if (!host) return;
    var stack = document.createElement('div');
    stack.className = 'console-collapsed-stack';
    stack.setAttribute('aria-hidden', 'true');
    stack.innerHTML = '' +
      '<span class="console-collapsed-logo"><i class="fa-solid fa-bug" style="color:#06b6d4;"></i></span>' +
      '<button class="console-collapsed-de" type="button" tabindex="-1">Bugence DE <i class="fa-solid fa-chevron-down"></i></button>';
    host.insertBefore(stack, host.firstChild);
  }

  function ensureEdgeToggle(sidebar) {
    if (document.getElementById('consoleSidebarEdgeToggle')) return;
    if (!sidebar) return;
    if (!sidebar.style.position) {
      sidebar.style.position = 'relative';
    }
    var btn = document.createElement('button');
    btn.type = 'button';
    btn.id = 'consoleSidebarEdgeToggle';
    btn.className = 'console-sidebar-edge-toggle';
    btn.setAttribute('aria-label', 'Toggle sidebar');
    btn.innerHTML = '<i class="fa-solid fa-chevron-left"></i>';
    sidebar.appendChild(btn);
  }

  function init() {
    if (document.getElementById('consoleSidebarToggle')) return;
    if (document.getElementById('appSidebar')) return;

    var sidebar = pickSidebar();
    if (!sidebar) return;

    var container = sidebar.parentElement;
    if (!container) return;

    var siblings = Array.prototype.slice.call(container.children).filter(function (el) {
      return el !== sidebar;
    });

    var main = siblings.find(function (el) {
      return el.tagName && el.tagName.toLowerCase() === 'main';
    }) || siblings[0];

    if (!main) return;

    document.body.classList.add('console-has-mobile-sidebar');
    document.body.classList.add('console-unified-sidebar');
    sidebar.classList.add('console-sidebar');
    main.classList.add('console-main');

    normalizeUnifiedMenu(sidebar);
    markActiveNav(sidebar);
    ensureCollapsedHeader(sidebar);
    ensureDesktopCollapseToggle(sidebar);
    ensureEdgeToggle(sidebar);

    var toggle = document.createElement('button');
    toggle.type = 'button';
    toggle.className = 'console-sidebar-toggle';
    toggle.id = 'consoleSidebarToggle';
    toggle.setAttribute('aria-label', 'Open sidebar');
    toggle.setAttribute('aria-expanded', 'false');
    toggle.innerHTML = '<i class="fa-solid fa-bars"></i>';

    var overlay = document.createElement('div');
    overlay.className = 'console-sidebar-overlay';
    overlay.id = 'consoleSidebarOverlay';

    document.body.appendChild(toggle);
    document.body.appendChild(overlay);

    function setOpen(open) {
      document.body.classList.toggle('console-sidebar-open', !!open);
      toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
    }

    function setDesktopCollapsed(collapsed) {
      document.body.classList.toggle('console-sidebar-collapsed', !!collapsed);
      try { localStorage.setItem(SIDEBAR_STATE_KEY, collapsed ? '1' : '0'); } catch (_) { }
    }

    function getSavedCollapsed() {
      try {
        var raw = localStorage.getItem(SIDEBAR_STATE_KEY);
        if (raw === '1') return true;
        if (raw === '0') return false;
      } catch (_) { }
      return true;
    }

    function syncViewportState() {
      if (!isMobile()) {
        setOpen(false);
        toggle.style.display = 'none';
        overlay.style.display = 'none';
        setDesktopCollapsed(getSavedCollapsed());
      } else {
        setDesktopCollapsed(false);
        toggle.style.display = 'inline-flex';
        overlay.style.display = 'block';
        setOpen(false);
      }
    }

    toggle.addEventListener('click', function () {
      setOpen(!document.body.classList.contains('console-sidebar-open'));
    });

    overlay.addEventListener('click', function () {
      setOpen(false);
    });

    document.addEventListener('keydown', function (event) {
      if (event.key === 'Escape' && isMobile()) {
        setOpen(false);
      }
    });

    var collapseBtn = document.getElementById('consoleSidebarCollapseBtn');
    var edgeBtn = document.getElementById('consoleSidebarEdgeToggle');
    if (collapseBtn) {
      collapseBtn.addEventListener('click', function () {
        if (isMobile()) return;
        setDesktopCollapsed(!document.body.classList.contains('console-sidebar-collapsed'));
      });
    }
    if (edgeBtn) {
      edgeBtn.addEventListener('click', function () {
        if (isMobile()) return;
        setDesktopCollapsed(!document.body.classList.contains('console-sidebar-collapsed'));
      });
    }

    Array.prototype.slice.call(sidebar.querySelectorAll('.nav-item, .brand, .ph-nav-item, .ph-back')).forEach(function (item) {
      item.addEventListener('click', function () {
        if (isMobile()) setOpen(false);
      });
    });

    window.addEventListener('resize', syncViewportState);
    syncViewportState();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
