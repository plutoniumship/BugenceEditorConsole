// bugence-nav.ts — Tailwind only; all hovers/animations in TS

type Theme = 'light' | 'dark';
const GOLD = '#F4A460';

// ---------- Theme ----------
function applyTheme(mode: Theme): void {
  const root = document.documentElement;
  const sun  = document.getElementById('gold-sun');
  const moon = document.getElementById('gold-moon');
  if (mode === 'dark') { root.classList.add('dark');  sun?.classList.add('hidden');  moon?.classList.remove('hidden'); }
  else                 { root.classList.remove('dark'); moon?.classList.add('hidden'); sun?.classList.remove('hidden'); }
  localStorage.setItem('bugence-theme', mode);
}

const saved = localStorage.getItem('bugence-theme');
const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
const initial: Theme = (saved === 'light' || saved === 'dark') ? (saved as Theme) : (prefersDark ? 'dark' : 'light');
applyTheme(initial);

// ---------- Elements ----------
const shell    = document.querySelector<HTMLDivElement>('#gold-shell');
const centerUl = document.querySelector<HTMLUListElement>('#gold-links');
const linksArr = centerUl ? Array.from(centerUl.querySelectorAll<HTMLAnchorElement>('a[data-shimmer]')) : [];
const cta      = document.querySelector<HTMLAnchorElement>('#gold-cta');
const burger   = document.querySelector<HTMLButtonElement>('#gold-burger');

const sub      = document.querySelector<HTMLDivElement>('#gold-sub');
const subPanel = document.querySelector<HTMLDivElement>('#gold-sub-panel');
const subBack  = document.querySelector<HTMLButtonElement>('#gold-sub-backdrop');
const subClose = document.querySelector<HTMLButtonElement>('#gold-sub-close');
const smTheme  = document.querySelector<HTMLButtonElement>('#gold-theme-sm');
const themeBtn = document.querySelector<HTMLButtonElement>('#gold-theme');

// ---------- Scroll polish ----------
function onScroll() {
  if (!shell) return;
  const s = window.scrollY > 8;
  shell.classList.toggle('bg-white/80', !s);
  shell.classList.toggle('bg-white/90', s);
  shell.classList.toggle('shadow-[0_14px_40px_-16px_rgba(0,0,0,.35)]', !s);
  shell.classList.toggle('shadow-[0_18px_60px_-18px_rgba(0,0,0,.45)]', s);
}
onScroll();
window.addEventListener('scroll', onScroll, { passive: true });

// ---------- Active link (static gold pill) ----------
let activeLink: HTMLAnchorElement | null = null;
{
  const path = location.pathname.replace(/index\.html?$/i, '') || '/';
  activeLink = linksArr.find(a => {
    const href = (a.getAttribute('href') || '').split('?')[0];
    return href === path || path.endsWith(href);
  }) || null;

  if (activeLink) {
    activeLink.setAttribute('aria-current', 'page');
    // static gold pill (no animation)
    activeLink.style.backgroundColor = GOLD;
    activeLink.style.borderRadius = '9999px';
    activeLink.style.color = '#0b0b0b';
  }
}

// ---------- Submenu (center sheet) ----------
const focusSel = 'a[href],button:not([disabled]),[tabindex]:not([tabindex="-1"])';
const getFocusables = (root: HTMLElement) =>
  Array.from(root.querySelectorAll<HTMLElement>(focusSel)).filter(el => el.offsetParent !== null);

let releaseTrap: (() => void) | null = null;

function enableTrap(container: HTMLElement) {
  const nodes = getFocusables(container);
  if (!nodes.length) return;
  const first = nodes[0], last = nodes[nodes.length - 1];
  const onKey = (e: KeyboardEvent) => {
    if (e.key !== 'Tab') return;
    if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
    else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
  };
  document.addEventListener('keydown', onKey);
  releaseTrap = () => document.removeEventListener('keydown', onKey);
  first.focus({ preventScroll: true });
}

function openSub() {
  if (!sub || !subPanel || !burger) return;
  sub.classList.remove('hidden');
  requestAnimationFrame(() => {
    subPanel.style.opacity = '1';
    subPanel.style.transform = 'translateX(-50%) scale(1)';
    burger.setAttribute('aria-expanded', 'true');
    document.documentElement.style.overflow = 'hidden';
    enableTrap(subPanel);
  });
}

function closeSub() {
  if (!sub || !subPanel || !burger) return;
  subPanel.style.opacity = '0';
  subPanel.style.transform = 'translateX(-50%) scale(0.95)';
  burger.setAttribute('aria-expanded', 'false');
  setTimeout(() => {
    sub.classList.add('hidden');
    document.documentElement.style.overflow = '';
    releaseTrap?.(); releaseTrap = null;
    burger.focus({ preventScroll: true });
  }, 160);
}

burger?.addEventListener('click', () => {
  const open = burger.getAttribute('aria-expanded') === 'true';
  open ? closeSub() : openSub();
});
subBack?.addEventListener('click', closeSub);
subClose?.addEventListener('click', closeSub);
window.addEventListener('keydown', (e) => { if (e.key === 'Escape' && sub && !sub.classList.contains('hidden')) closeSub(); });
sub?.addEventListener('click', (e) => { if ((e.target as HTMLElement).closest('a')) closeSub(); });

// Auto-close submenu when switching to desktop width
const mqDesktop = window.matchMedia('(min-width: 1280px)');
const mqHandler = (e: MediaQueryListEvent | MediaQueryList) => { if ('matches' in e ? e.matches : (e as MediaQueryList).matches) closeSub(); };
mqDesktop.addEventListener?.('change', mqHandler);
mqDesktop.addListener?.(mqHandler);

// ---------- Theme toggle ----------
function currentTheme(): Theme {
  return document.documentElement.classList.contains('dark') ? 'dark' : 'light';
}
function flipTheme() { applyTheme(currentTheme() === 'dark' ? 'light' : 'dark'); }
themeBtn?.addEventListener('click', flipTheme);
smTheme?.addEventListener('click', flipTheme);

// ---------- Gold shimmer (JS-driven; no CSS keyframes) ----------
type ShimmerHandle = { t?: number; x: number };
function setShimmerBG(el: HTMLElement) {
  // single-color “gradient” using GOLD only
  el.style.backgroundImage = `linear-gradient(110deg, rgba(244,164,96,0.85) 0%, rgba(244,164,96,1) 50%, rgba(244,164,96,0.85) 100%)`;
  el.style.backgroundSize = '200% 200%';
  el.style.borderRadius = '9999px';
  el.style.color = '#0b0b0b';
}
function clearShimmerBG(el: HTMLElement) {
  el.style.backgroundImage = '';
  el.style.backgroundSize = '';
  el.style.backgroundPosition = '';
  el.style.color = '';
}
function startShimmer(el: HTMLElement, handle: ShimmerHandle) {
  setShimmerBG(el);
  const go = () => {
    handle.x = (handle.x + 1.2) % 200; // speed
    el.style.backgroundPosition = `${handle.x}% 50%`;
    handle.t = requestAnimationFrame(go);
  };
  handle.t = requestAnimationFrame(go);
}
function stopShimmer(el: HTMLElement, handle: ShimmerHandle) {
  if (handle.t) cancelAnimationFrame(handle.t);
  handle.t = undefined; handle.x = 0;
  clearShimmerBG(el);
}
function attachShimmer(a: HTMLElement) {
  const h: ShimmerHandle = { x: 0 };
  a.addEventListener('mouseenter', () => startShimmer(a, h));
  a.addEventListener('mouseleave', () => stopShimmer(a, h));
  a.addEventListener('focus',      () => startShimmer(a, h));
  a.addEventListener('blur',       () => stopShimmer(a, h));
}

// Attach shimmer to non-active items only
document.querySelectorAll<HTMLElement>('[data-shimmer]').forEach(el => {
  if (el.getAttribute('aria-current') !== 'page') attachShimmer(el);
});

// CTA: always gold base, shimmer on hover
if (cta) {
  cta.style.backgroundColor = GOLD;
  attachShimmer(cta);
}

// ---------- Homepage URL cleanup (hide /index.html) ----------
if (/\/index\.html?$/i.test(location.pathname)) {
  history.replaceState({}, '', location.pathname.replace(/index\.html?$/i, '') || '/');
}
