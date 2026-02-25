"use strict";
const GOLD = '#F4A460';
function applyTheme(mode) {
    const root = document.documentElement;
    const sun = document.getElementById('gold-sun');
    const moon = document.getElementById('gold-moon');
    if (mode === 'dark') {
        root.classList.add('dark');
        sun?.classList.add('hidden');
        moon?.classList.remove('hidden');
    }
    else {
        root.classList.remove('dark');
        moon?.classList.add('hidden');
        sun?.classList.remove('hidden');
    }
    localStorage.setItem('bugence-theme', mode);
}
const saved = localStorage.getItem('bugence-theme');
const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
const initial = (saved === 'light' || saved === 'dark') ? saved : (prefersDark ? 'dark' : 'light');
applyTheme(initial);
const shell = document.querySelector('#gold-shell');
const centerUl = document.querySelector('#gold-links');
const linksArr = centerUl ? Array.from(centerUl.querySelectorAll('a[data-shimmer]')) : [];
const cta = document.querySelector('#gold-cta');
const burger = document.querySelector('#gold-burger');
const sub = document.querySelector('#gold-sub');
const subPanel = document.querySelector('#gold-sub-panel');
const subBack = document.querySelector('#gold-sub-backdrop');
const subClose = document.querySelector('#gold-sub-close');
const smTheme = document.querySelector('#gold-theme-sm');
const themeBtn = document.querySelector('#gold-theme');
function onScroll() {
    if (!shell)
        return;
    const s = window.scrollY > 8;
    shell.classList.toggle('bg-white/80', !s);
    shell.classList.toggle('bg-white/90', s);
    shell.classList.toggle('shadow-[0_14px_40px_-16px_rgba(0,0,0,.35)]', !s);
    shell.classList.toggle('shadow-[0_18px_60px_-18px_rgba(0,0,0,.45)]', s);
}
onScroll();
window.addEventListener('scroll', onScroll, { passive: true });
let activeLink = null;
{
    const path = location.pathname.replace(/index\.html?$/i, '') || '/';
    activeLink = linksArr.find(a => {
        const href = (a.getAttribute('href') || '').split('?')[0];
        return href === path || path.endsWith(href);
    }) || null;
    if (activeLink) {
        activeLink.setAttribute('aria-current', 'page');
        activeLink.style.backgroundColor = GOLD;
        activeLink.style.borderRadius = '9999px';
        activeLink.style.color = '#0b0b0b';
    }
}
const focusSel = 'a[href],button:not([disabled]),[tabindex]:not([tabindex="-1"])';
const getFocusables = (root) => Array.from(root.querySelectorAll(focusSel)).filter(el => el.offsetParent !== null);
let releaseTrap = null;
function enableTrap(container) {
    const nodes = getFocusables(container);
    if (!nodes.length)
        return;
    const first = nodes[0], last = nodes[nodes.length - 1];
    const onKey = (e) => {
        if (e.key !== 'Tab')
            return;
        if (e.shiftKey && document.activeElement === first) {
            e.preventDefault();
            last.focus();
        }
        else if (!e.shiftKey && document.activeElement === last) {
            e.preventDefault();
            first.focus();
        }
    };
    document.addEventListener('keydown', onKey);
    releaseTrap = () => document.removeEventListener('keydown', onKey);
    first.focus({ preventScroll: true });
}
function openSub() {
    if (!sub || !subPanel || !burger)
        return;
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
    if (!sub || !subPanel || !burger)
        return;
    subPanel.style.opacity = '0';
    subPanel.style.transform = 'translateX(-50%) scale(0.95)';
    burger.setAttribute('aria-expanded', 'false');
    setTimeout(() => {
        sub.classList.add('hidden');
        document.documentElement.style.overflow = '';
        releaseTrap?.();
        releaseTrap = null;
        burger.focus({ preventScroll: true });
    }, 160);
}
burger?.addEventListener('click', () => {
    const open = burger.getAttribute('aria-expanded') === 'true';
    open ? closeSub() : openSub();
});
subBack?.addEventListener('click', closeSub);
subClose?.addEventListener('click', closeSub);
window.addEventListener('keydown', (e) => { if (e.key === 'Escape' && sub && !sub.classList.contains('hidden'))
    closeSub(); });
sub?.addEventListener('click', (e) => { if (e.target.closest('a'))
    closeSub(); });
const mqDesktop = window.matchMedia('(min-width: 1280px)');
const mqHandler = (e) => { if ('matches' in e ? e.matches : e.matches)
    closeSub(); };
mqDesktop.addEventListener?.('change', mqHandler);
mqDesktop.addListener?.(mqHandler);
function currentTheme() {
    return document.documentElement.classList.contains('dark') ? 'dark' : 'light';
}
function flipTheme() { applyTheme(currentTheme() === 'dark' ? 'light' : 'dark'); }
themeBtn?.addEventListener('click', flipTheme);
smTheme?.addEventListener('click', flipTheme);
function setShimmerBG(el) {
    el.style.backgroundImage = `linear-gradient(110deg, rgba(244,164,96,0.85) 0%, rgba(244,164,96,1) 50%, rgba(244,164,96,0.85) 100%)`;
    el.style.backgroundSize = '200% 200%';
    el.style.borderRadius = '9999px';
    el.style.color = '#0b0b0b';
}
function clearShimmerBG(el) {
    el.style.backgroundImage = '';
    el.style.backgroundSize = '';
    el.style.backgroundPosition = '';
    el.style.color = '';
}
function startShimmer(el, handle) {
    setShimmerBG(el);
    const go = () => {
        handle.x = (handle.x + 1.2) % 200;
        el.style.backgroundPosition = `${handle.x}% 50%`;
        handle.t = requestAnimationFrame(go);
    };
    handle.t = requestAnimationFrame(go);
}
function stopShimmer(el, handle) {
    if (handle.t)
        cancelAnimationFrame(handle.t);
    handle.t = undefined;
    handle.x = 0;
    clearShimmerBG(el);
}
function attachShimmer(a) {
    const h = { x: 0 };
    a.addEventListener('mouseenter', () => startShimmer(a, h));
    a.addEventListener('mouseleave', () => stopShimmer(a, h));
    a.addEventListener('focus', () => startShimmer(a, h));
    a.addEventListener('blur', () => stopShimmer(a, h));
}
document.querySelectorAll('[data-shimmer]').forEach(el => {
    if (el.getAttribute('aria-current') !== 'page')
        attachShimmer(el);
});
if (cta) {
    cta.style.backgroundColor = GOLD;
    attachShimmer(cta);
}
if (/\/index\.html?$/i.test(location.pathname)) {
    history.replaceState({}, '', location.pathname.replace(/index\.html?$/i, '') || '/');
}
