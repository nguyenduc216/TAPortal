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

    const flash = (workspace, alert) => {
        alert.classList.add('ta-rv-flash');
        alert.removeAttribute('data-ta-toast');
        workspace.before(alert);
        window.setTimeout(() => alert.remove(), 4000);
    };
})();
