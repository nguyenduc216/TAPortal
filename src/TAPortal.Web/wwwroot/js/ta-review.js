// Hợp đồng dịch vụ: review the contract in place. Clicking a yellow mark (or ✎ on a paragraph, or a change in "Thay đổi"
// mode) opens its decision panel (template#pop-<id>) next to it. The panel's forms post with fetch; the workspace is then
// re-read and swapped in place, so the decided text changes colour without a page jump. Loaded on every page (the shell
// swaps <main> without running page scripts), so everything uses delegated listeners and is idle elsewhere.
(() => {
    'use strict';
    let pop = null, opener = null;

    const close = () => {
        pop?.remove();
        pop = null;
        opener?.classList.remove('is-open');
        opener = null;
    };

    const place = (panel, anchor) => {
        const r = anchor.getBoundingClientRect();
        const width = Math.min(440, document.documentElement.clientWidth - 24);
        panel.style.width = width + 'px';
        let left = r.left + window.scrollX;
        left = Math.max(12 + window.scrollX, Math.min(left, window.scrollX + document.documentElement.clientWidth - width - 12));
        panel.style.left = left + 'px';
        panel.style.top = (r.bottom + window.scrollY + 8) + 'px';
    };

    const open = (anchor) => {
        const id = anchor.getAttribute('data-ta-pop');
        const template = document.getElementById('pop-' + id);
        if (!template) return;
        if (opener === anchor) { close(); return; }
        close();
        pop = document.createElement('div');
        pop.className = 'ta-rv-pop';
        pop.setAttribute('role', 'dialog');
        pop.setAttribute('aria-label', 'Chi tiết góp ý');
        const shut = document.createElement('button');
        shut.type = 'button';
        shut.className = 'ta-rv-pop__close';
        shut.setAttribute('aria-label', 'Đóng');
        shut.textContent = '×';
        shut.addEventListener('click', close);
        pop.append(shut, template.content.cloneNode(true));
        document.body.append(pop);
        place(pop, anchor);
        opener = anchor;
        anchor.classList.add('is-open');
        (pop.querySelector('[data-ta-review-accept]') || pop.querySelector('button:not(.ta-rv-pop__close)'))?.focus({ preventScroll: true });
    };

    // In-page links (#p-P54, #f-…, #c-…) scroll to their target here: changing location.hash would fire popstate, which
    // the shell answers with a reload scrolled to the top.
    const jump = (id) => {
        const target = document.getElementById(id);
        if (!target) return false;
        const details = target.matches('details') ? target : target.querySelector(':scope > details');
        if (details) details.open = true;
        target.scrollIntoView({ behavior: 'smooth', block: 'center' });
        target.classList.remove('is-jump');
        void target.offsetWidth;
        target.classList.add('is-jump');
        return true;
    };
    document.addEventListener('click', (event) => {
        const link = event.target.closest?.('[data-ta-review-workspace] a[href^="#"], [data-ta-legal-articles] a[href^="#"]');
        if (!link || link.getAttribute('href').length < 2) return;
        if (jump(link.getAttribute('href').slice(1))) { event.preventDefault(); event.stopPropagation(); close(); }
    }, true);
    // A law article opened from a citation link (…/LegalLibrary/Details/<doc>#a-<article>) is expanded and shown.
    const openHash = () => {
        if (window.location.hash.startsWith('#a-')) window.setTimeout(() => jump(window.location.hash.slice(1)), 50);
    };
    document.addEventListener('ta:load', openHash);
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', openHash); else openHash();

    document.addEventListener('click', (event) => {
        const anchor = event.target.closest?.('[data-ta-review-workspace] [data-ta-pop]');
        if (anchor) { event.preventDefault(); open(anchor); return; }
        if (pop && !pop.contains(event.target)) close();
    });
    document.addEventListener('keydown', (event) => {
        if (event.key === 'Escape' && pop) { const back = opener; close(); back?.focus(); return; }
        const anchor = event.target.closest?.('[data-ta-review-workspace] [data-ta-pop]');
        if (anchor && (event.key === 'Enter' || event.key === ' ') && anchor.tagName !== 'BUTTON') { event.preventDefault(); open(anchor); }
    });
    window.addEventListener('resize', () => { if (pop && opener) place(pop, opener); });
    document.addEventListener('ta:before-swap', close);

    // Decision forms: post, then re-read the page and swap only the workspace (scroll position kept).
    document.addEventListener('submit', async (event) => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement) || !form.hasAttribute('data-ta-review-form') || !window.fetch || !window.DOMParser) return;
        const workspace = document.querySelector('[data-ta-review-workspace]');
        if (!workspace) return;
        event.preventDefault();
        if (form.dataset.taBusy === 'true') return;
        form.dataset.taBusy = 'true';
        const buttons = [...form.querySelectorAll('button')];
        buttons.forEach((b) => { b.disabled = true; });
        try {
            const posted = await fetch(form.action, { method: 'POST', body: new FormData(form, event.submitter), credentials: 'same-origin' });
            if (!posted.ok) throw new Error(String(posted.status));
            const message = new DOMParser().parseFromString(await posted.text(), 'text/html').querySelector('[data-ta-toast]');
            const fresh = await fetch(window.location.href, { credentials: 'same-origin', headers: { 'X-TA-Soft': '1' } });
            const doc = new DOMParser().parseFromString(await fresh.text(), 'text/html');
            const next = doc.querySelector('[data-ta-review-workspace]');
            if (!next) { window.location.reload(); return; }
            close();
            const y = window.scrollY;
            const current = document.querySelector('[data-ta-review-workspace]');
            const imported = document.importNode(next, true);
            current.replaceWith(imported);
            window.scrollTo(0, y);
            if (message) flash(imported, document.importNode(message, true));
        } catch {
            buttons.forEach((b) => { b.disabled = false; });
            let error = form.querySelector('.ta-rv-pop__error');
            if (!error) { error = document.createElement('p'); error.className = 'ta-rv-pop__error'; form.append(error); }
            error.textContent = 'Không lưu được, vui lòng thử lại (hoặc tải lại trang).';
        } finally {
            delete form.dataset.taBusy;
        }
    }, true);

    const flash = (workspace, alert, ms = 4000) => {
        alert.classList.add('ta-rv-flash');
        alert.removeAttribute('data-ta-toast');
        workspace.before(alert);
        window.setTimeout(() => alert.remove(), ms);
    };

    // ---- Compare view: show only suggested paragraphs, step between them ------------------------------------------------
    let cmpIndex = -1;
    const suggestions = () => [...document.querySelectorAll('[data-ta-review-side-by-side] [data-ta-sugg="true"]')];
    const showPos = () => {
        const pos = document.querySelector('[data-ta-compare-pos]');
        const all = suggestions();
        if (pos) pos.textContent = all.length ? `${Math.max(cmpIndex, 0) + 1}/${all.length}` : '0/0';
    };
    document.addEventListener('change', (event) => {
        if (!event.target.matches?.('[data-ta-compare-only]')) return;
        event.target.closest('[data-ta-review-side-by-side]')?.classList.toggle('is-only-changed', event.target.checked);
    });
    document.addEventListener('click', (event) => {
        const step = event.target.closest?.('[data-ta-compare-step]');
        if (!step) return;
        const all = suggestions();
        if (all.length === 0) return;
        all[cmpIndex]?.classList.remove('is-current');
        cmpIndex = (cmpIndex + Number(step.dataset.taCompareStep) + all.length) % all.length;
        all[cmpIndex].classList.add('is-current');
        all[cmpIndex].scrollIntoView({ behavior: 'smooth', block: 'center' });
        showPos();
    });
    document.addEventListener('ta:load', () => { cmpIndex = -1; showPos(); });
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', showPos); else showPos();
    new MutationObserver(() => { if (document.querySelector('[data-ta-compare-pos]')?.textContent === '—') showPos(); })
        .observe(document.documentElement, { childList: true, subtree: true });

    // ---- AI run in progress: elapsed time every second, status every 10 seconds, page refreshed in place when done ----
    // One timer for the whole app; it only acts while a [data-ta-review-running] block is on the page (soft navigation
    // keeps scripts alive, so it simply goes idle elsewhere).
    let lastPoll = 0, polling = false;
    const two = (n) => String(n).padStart(2, '0');
    const refresh = async (done) => {
        const fresh = await fetch(window.location.href, { credentials: 'same-origin', headers: { 'X-TA-Soft': '1' } });
        const doc = new DOMParser().parseFromString(await fresh.text(), 'text/html');
        const next = doc.querySelector('[data-ta-review-workspace]');
        const current = document.querySelector('[data-ta-review-workspace]');
        if (!next || !current) { window.location.reload(); return; }
        close();
        const y = window.scrollY;
        const imported = document.importNode(next, true);
        current.replaceWith(imported);
        window.scrollTo(0, y);
        const alert = document.createElement('div');
        alert.className = 'ta-alert ' + (done.ok ? 'ta-alert--success' : 'ta-alert--danger');
        alert.setAttribute('role', 'status');
        alert.textContent = done.ok ? `AI đã rà soát xong: ${done.findings ?? 0} góp ý. Bấm vào chỗ tô vàng để xử lý.` : `AI rà soát không thành công: ${done.error || 'xem mục "Lượt AI rà soát".'}`;
        flash(imported, alert, 8000);
    };
    window.setInterval(async () => {
        const box = document.querySelector('[data-ta-review-running]');
        if (!box) return;
        const since = Date.parse(box.dataset.taSince || '');
        const elapsed = box.querySelector('[data-ta-elapsed]');
        if (elapsed && !Number.isNaN(since)) {
            const s = Math.max(0, Math.floor((Date.now() - since) / 1000));
            elapsed.textContent = s >= 60 ? `${Math.floor(s / 60)} phút ${two(s % 60)} giây` : `${s} giây`;
        }
        if (polling || Date.now() - lastPoll < 10000 || document.hidden || !window.fetch) return;
        polling = true;
        lastPoll = Date.now();
        try {
            const response = await fetch(box.dataset.taStatusUrl, { credentials: 'same-origin', headers: { Accept: 'application/json' } });
            if (!response.ok) return;
            const status = await response.json();
            if (status.active) {
                const text = box.querySelector('[data-ta-running-text]');
                if (text && status.runStatus === 'RUNNING') text.textContent = 'AI đang đọc và rà soát hợp đồng…';
                if (status.since) box.dataset.taSince = status.since;
                const tokens = box.querySelector('[data-ta-tokens]');
                const n = (v) => Number(v).toLocaleString('vi-VN');
                if (tokens && status.inputTokens) tokens.textContent = `đã đọc ${n(status.inputTokens)} token` + (status.outputTokens ? ` · đã viết ~${n(status.outputTokens)} token` : ' · AI đang suy nghĩ');
                return;
            }
            await refresh({ ok: status.runStatus === 'DONE', findings: status.findings, error: status.error });
        } catch { /* network hiccup: try again at the next poll */ } finally { polling = false; }
    }, 1000);
})();
