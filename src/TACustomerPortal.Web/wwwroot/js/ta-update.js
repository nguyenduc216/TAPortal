// Connection / update notice: the recovery box of the former Blazor Admin (logo inside a spinning ring, "Đang kết nối lại",
// "Hệ thống đang được cập nhật"), rebuilt for the MVC apps. Shared by the Admin and Customer Portal layouts (byte-identical
// copies, like ta-shell.js); each layout passes its own logo in <meta name="ta-build" data-logo>, so the Customer Portal
// shows the Hóa đơn tự động logo. Styles are set from script (no inline <style>, CSP-safe) so both designs get the same box.
// The layout renders the running build in <meta name="ta-build">; while the tab is visible this script asks GET /_build
// every few seconds.
// - No answer: "Đang kết nối lại"; after a few misses "Hệ thống đang được cập nhật" (HTTP 502/503/504: app pool stopped by a
//   deploy) or "Hệ thống đang gián đoạn" (no answer at all). Retries continue every 2 s; "Thử kết nối lại" checks at once.
// - Answer again with the same build: "Kết nối đã khôi phục" (green ring), the box closes by itself.
//   With another build (the server was updated): "Máy chủ đã hoạt động trở lại" and the page reloads itself.
// - An update noticed between two checks: the page reloads, unless the person has typed into a form, then a bar offers
//   "Tải lại" instead of discarding what they typed.
// ta-shell.js asks window.taUpdate.unavailable(result) before falling back to a full page load on a failed navigation.
(() => {
    'use strict';
    const meta = document.querySelector('meta[name="ta-build"]');
    if (!meta || !window.fetch) return;
    const url = meta.dataset.url || '/_build';
    const logo = meta.dataset.logo || '';
    const known = meta.content;
    const NORMAL = 5000, RETRY = 2000, SLOW_AFTER = 3;
    const copy = {
        reconnecting: ['Đang kết nối lại', 'Hệ thống đang kết nối. Vui lòng chờ trong ít phút...'],
        maintenance: ['Hệ thống đang được cập nhật', 'Hệ thống đang được bảo trì và sẽ hoạt động trở lại trong ít phút. Vui lòng chờ...'],
        disconnected: ['Hệ thống đang gián đoạn', 'Không thể kết nối với máy chủ. Hệ thống đang tự động kiểm tra và thử kết nối lại.'],
        recovered: ['Kết nối đã khôi phục', 'Hệ thống đã kết nối lại với máy chủ và sẵn sàng hoạt động.'],
        restart: ['Máy chủ đã hoạt động trở lại', 'Đang tải lại phiên bản mới...'],
    };
    const RING = 'conic-gradient(from 0deg, #e6edf1 12%, #e4bd43 48%, #147b9a 80%, #07345c)';
    const font = '15px/1.5 system-ui, -apple-system, "Segoe UI", Roboto, Arial, sans-serif';
    const reduceMotion = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
    let timer = 0, dismiss = 0, misses = 0, state = 'hidden', host = null, bar = null, spin = null;
    let dirty = false, reloading = false, busy = false, lastStatus = null, previousFocus = null;

    const el = (tag, style, text) => { const e = document.createElement(tag); Object.assign(e.style, style); if (text) e.textContent = text; return e; };

    const button = (text, onClick) => {
        const b = el('button', { border: '0', borderRadius: '8px', padding: '8px 16px', background: '#0F64C6', color: '#fff', font, fontWeight: '600', cursor: 'pointer' }, text);
        b.type = 'button';
        b.addEventListener('click', onClick);
        return b;
    };

    const ensureHost = () => {
        if (host?.isConnected) return;
        host = el('div', { position: 'fixed', inset: '0', zIndex: '2147483000', display: 'none', alignItems: 'center', justifyContent: 'center', padding: '16px', background: 'rgba(9, 47, 82, .72)', backdropFilter: 'blur(3px)' });
        const card = el('div', { boxSizing: 'border-box', width: 'min(100%, 420px)', padding: '32px 24px', textAlign: 'center', background: '#fff', color: '#0f172a', borderRadius: '16px', boxShadow: '0 16px 48px rgba(0,0,0,.24)', font, outline: 'none' });
        card.setAttribute('role', 'dialog');
        card.setAttribute('aria-modal', 'true');
        card.setAttribute('aria-labelledby', 'ta-recovery-title');
        card.setAttribute('aria-describedby', 'ta-recovery-message');
        card.tabIndex = -1;
        const visual = el('div', { position: 'relative', display: 'grid', placeItems: 'center', width: '88px', height: '88px', margin: '0 auto 16px', isolation: 'isolate' });
        visual.setAttribute('aria-hidden', 'true');
        spin = el('span', { position: 'absolute', inset: '0', borderRadius: '50%', background: RING, mask: 'radial-gradient(farthest-side, transparent calc(100% - 4px), #000 0)', webkitMask: 'radial-gradient(farthest-side, transparent calc(100% - 4px), #000 0)' });
        if (!reduceMotion) spin.animate?.([{ transform: 'rotate(0deg)' }, { transform: 'rotate(360deg)' }], { duration: 1200, iterations: Infinity });
        visual.append(spin);
        if (logo) {
            const img = el('img', { position: 'relative', width: '52px', height: '52px', objectFit: 'contain' });
            img.src = logo; img.alt = ''; img.width = 52; img.height = 52;
            visual.append(img);
        }
        const title = el('h2', { margin: '0 0 6px', fontSize: '19px', fontWeight: '700', color: '#0f172a' });
        title.id = 'ta-recovery-title';
        const message = el('p', { margin: '0 0 6px', color: '#334155' });
        message.id = 'ta-recovery-message';
        message.setAttribute('aria-live', 'polite');
        const detail = el('p', { margin: '0 0 14px', minHeight: '20px', color: '#65758a', fontSize: '13px' });
        const retry = button('Thử kết nối lại', () => { misses = Math.min(misses, 1); check(); });
        card.append(visual, title, message, detail, retry);
        host.append(card);
        host._parts = { card, title, message, detail, retry };
        document.body.append(host);
        // Fetch the logo now, while the server still answers: during an outage it could not load.
        if (logo) new Image().src = logo;
    };

    const setState = (next) => {
        if (next === state) { if (next !== 'hidden') host._parts.detail.textContent = misses > 1 && next !== 'recovered' && next !== 'restart' ? `Lần thử ${misses}` : ''; return; }
        ensureHost();
        window.clearTimeout(dismiss);
        if (state === 'hidden' && next !== 'hidden') previousFocus = document.activeElement;
        state = next;
        const p = host._parts;
        if (next === 'hidden') {
            host.style.display = 'none';
            if (previousFocus?.isConnected) previousFocus.focus({ preventScroll: true });
            previousFocus = null;
            return;
        }
        host.style.display = 'flex';
        p.title.textContent = copy[next][0];
        p.message.textContent = copy[next][1];
        p.detail.textContent = misses > 1 && next !== 'recovered' && next !== 'restart' ? `Lần thử ${misses}` : '';
        p.retry.style.display = next === 'recovered' || next === 'restart' ? 'none' : '';
        spin.style.background = next === 'recovered' ? '#2ba374' : next === 'restart' ? '#d9a400' : RING;
        p.card.focus({ preventScroll: true });
        if (next === 'recovered') dismiss = window.setTimeout(() => { if (state === 'recovered') setState('hidden'); }, 1500);
    };

    const showBar = () => {
        if (bar) return;
        bar = el('div', { position: 'fixed', left: '50%', bottom: '16px', transform: 'translateX(-50%)', zIndex: '2147483000', display: 'flex', alignItems: 'center', gap: '12px', padding: '10px 12px 10px 16px', borderRadius: '10px', background: '#0f172a', color: '#fff', boxShadow: '0 10px 30px rgba(0,0,0,.25)', font, fontSize: '14px', maxWidth: 'calc(100% - 32px)' });
        bar.setAttribute('role', 'status');
        bar.append(el('span', {}, 'Hệ thống vừa được cập nhật.'), button('Tải lại', () => window.location.reload()));
        document.body.append(bar);
    };

    const reload = () => {
        if (reloading) return;
        reloading = true;
        if (state !== 'hidden') { setState('restart'); window.setTimeout(() => window.location.reload(), 800); }
        else window.location.reload();
    };
    const schedule = (delay) => { window.clearTimeout(timer); if (!document.hidden || state !== 'hidden') timer = window.setTimeout(check, delay); };

    const check = async () => {
        window.clearTimeout(timer);
        if (busy || reloading) return;
        busy = true;
        let build = null;
        lastStatus = null;
        const controller = new AbortController();
        const timeout = window.setTimeout(() => controller.abort(), 4500);
        try {
            const response = await fetch(`${url}?_=${Date.now()}`, { cache: 'no-store', credentials: 'same-origin', headers: { Accept: 'application/json' }, signal: controller.signal });
            lastStatus = response.status;
            if (response.ok && (response.headers.get('content-type') || '').includes('json')) build = (await response.json()).build || null;
        } catch { /* server down or network gone */ }
        window.clearTimeout(timeout);
        busy = false;
        if (build === null) {
            misses++;
            const updating = lastStatus === 502 || lastStatus === 503 || lastStatus === 504;
            setState(misses < SLOW_AFTER && !updating ? 'reconnecting' : updating ? 'maintenance' : 'disconnected');
            schedule(RETRY);
            return;
        }
        const wasDown = state !== 'hidden' && state !== 'recovered';
        misses = 0;
        if (build !== known) {
            if (wasDown || !dirty) { reload(); return; }
            showBar();
        }
        if (wasDown) setState('recovered');
        schedule(NORMAL);
    };

    // A form someone has typed into is "dirty": an update noticed between two checks then asks before reloading.
    document.addEventListener('input', (event) => { if (event.target instanceof Element && event.target.closest('form')) dirty = true; }, true);
    document.addEventListener('ta:load', () => { dirty = false; });
    document.addEventListener('visibilitychange', () => { if (!document.hidden) check(); else if (state === 'hidden') window.clearTimeout(timer); });
    window.addEventListener('online', () => check());
    window.addEventListener('offline', () => { misses = Math.max(misses, 1); setState('reconnecting'); schedule(RETRY); });

    window.taUpdate = {
        check,
        // A soft navigation that got no answer or 502/503/504: show the box instead of the server's error page.
        unavailable: (result) => {
            if (result && ![502, 503, 504].includes(result.status)) return false;
            misses = Math.max(misses, 1);
            setState(result ? 'maintenance' : 'reconnecting');
            check();
            return true;
        },
    };
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', ensureHost); else ensureHost();
    schedule(NORMAL);
})();
