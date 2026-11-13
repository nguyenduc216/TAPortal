// T.A shell behaviour shared by TAPortal Admin and T.A Customer Portal (identical copy in both apps; a safe test compares them).
// Sidebar: desktop (>= 1200px) collapses to an icon rail and remembers it; tablet shows the rail and opens the full menu over
// the content; mobile (< 768px) uses an off-canvas drawer with overlay, close button and Escape. Also: password show/hide
// and live password rules shared by every password form; client-side list paging/filtering; soft navigation
// (prefetch + in-place content swap with a view transition, so pages, tabs and forms never feel like a server round trip).
(() => {
    'use strict';

    const body = document.body;
    const sidebar = document.getElementById('taSidebar');
    const overlay = document.querySelector('.ta-overlay');
    const toggles = document.querySelectorAll('[data-ta-sidebar-toggle]');
    const desktop = window.matchMedia('(min-width: 1200px)');
    const storageKey = 'ta.sidebar.collapsed';

    const readCollapsed = () => {
        try { return window.localStorage.getItem(storageKey) === '1'; } catch { return false; }
    };
    const writeCollapsed = (value) => {
        try { window.localStorage.setItem(storageKey, value ? '1' : '0'); } catch { /* storage unavailable: keep in-page state only */ }
    };
    const syncToggles = () => {
        const expanded = desktop.matches ? !body.classList.contains('ta-sidebar-collapsed') : body.classList.contains('ta-drawer-open');
        toggles.forEach((button) => {
            button.setAttribute('aria-expanded', String(expanded));
            button.setAttribute('aria-label', expanded ? 'Thu gọn menu' : 'Mở menu');
        });
    };
    const openDrawer = () => {
        body.classList.add('ta-drawer-open');
        if (overlay) overlay.hidden = false;
        syncToggles();
        sidebar?.querySelector('a[href], button')?.focus();
    };
    const closeDrawer = (returnFocus) => {
        if (!body.classList.contains('ta-drawer-open')) return;
        body.classList.remove('ta-drawer-open');
        if (overlay) overlay.hidden = true;
        syncToggles();
        if (returnFocus) toggles[0]?.focus();
    };

    if (sidebar) {
        if (readCollapsed()) body.classList.add('ta-sidebar-collapsed');
        toggles.forEach((button) => button.addEventListener('click', () => {
            if (desktop.matches) {
                writeCollapsed(body.classList.toggle('ta-sidebar-collapsed'));
                syncToggles();
            } else if (body.classList.contains('ta-drawer-open')) {
                closeDrawer(true);
            } else {
                openDrawer();
            }
        }));
        document.querySelectorAll('[data-ta-drawer-close]').forEach((element) => element.addEventListener('click', () => closeDrawer(true)));
        document.addEventListener('keydown', (event) => {
            if (event.key === 'Escape') closeDrawer(true);
        });
        desktop.addEventListener('change', () => {
            closeDrawer(false);
            syncToggles();
        });
        document.addEventListener('ta:load', () => closeDrawer(false));
        syncToggles();
    }

    // Live password rules, mirroring TAPortal.Shared.PasswordPolicy (char.IsLetter / IsDigit / !IsLetterOrDigit, so
    // Vietnamese letters such as "đ" count as letters, not special characters). Empty input: neutral; otherwise each
    // rule turns green (passed) or red (violated).
    window.taCheckPassword = (value, rulesId) => {
        const box = document.getElementById(rulesId);
        if (!box) return;
        const rules = { len: value.length >= 8, letter: /\p{L}/u.test(value), digit: /\p{Nd}/u.test(value), special: /[^\p{L}\p{Nd}]/u.test(value) };
        Object.entries(rules).forEach(([name, valid]) => {
            const item = box.querySelector(`[data-rule="${name}"]`);
            if (!item) return;
            item.classList.toggle('ok', value !== '' && valid);
            item.classList.toggle('fail', value !== '' && !valid);
        });
    };

    // Confirmation field: the "match" rule turns green when both values are equal, red when they differ (neutral while empty).
    window.taCheckConfirm = (passwordId, confirmId, ruleId) => {
        const password = document.getElementById(passwordId);
        const confirm = document.getElementById(confirmId);
        const rule = document.getElementById(ruleId);
        if (!password || !confirm || !rule) return;
        const value = confirm.value;
        rule.classList.toggle('ok', value !== '' && value === password.value);
        rule.classList.toggle('fail', value !== '' && value !== password.value);
    };

    window.taTogglePassword = (inputId, button) => {
        const input = document.getElementById(inputId);
        if (!input) return;
        const show = input.type === 'password';
        input.type = show ? 'text' : 'password';
        button.setAttribute('aria-label', show ? 'Ẩn mật khẩu' : 'Hiện mật khẩu');
        button.setAttribute('aria-pressed', String(show));
        button.classList.toggle('is-shown', show);
    };

    // ---- Lists: client-side filtering + pagination -------------------------------------------------------------
    // A list is any element with id X whose items carry data-ta-row (table rows or cards). data-ta-pager="10" on the
    // list enables paging; the bar is rendered into [data-ta-pager-for="X"]. Rows are already in the page, so changing
    // page or filtering is instant (no request). Page size is remembered per list (browser storage, optional).
    const fold = (text) => text.normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/đ/g, 'd').replace(/Đ/g, 'D').toLocaleLowerCase('vi');
    const pagers = new Map();
    const pageSizes = [10, 20, 50];
    const storedSize = (id) => { try { return Number(window.localStorage.getItem(`ta.pager.${id}`)) || 0; } catch { return 0; } };
    const storeSize = (id, size) => { try { window.localStorage.setItem(`ta.pager.${id}`, String(size)); } catch { /* optional */ } };
    const scopeOf = (list) => list.closest('[data-ta-list-scope]') || list.closest('.ta-table-card') || list.parentElement;
    const rowsOf = (list) => Array.from(list.querySelectorAll('[data-ta-row]'));

    const renderList = (list) => {
        const state = pagers.get(list.id);
        const rows = rowsOf(list);
        const matched = rows.filter((row) => row.dataset.taFiltered !== '1');
        const size = state ? state.size : Math.max(matched.length, 1);
        const pages = Math.max(1, Math.ceil(matched.length / size));
        if (state) state.page = Math.min(Math.max(1, state.page), pages);
        const page = state ? state.page : 1;
        const first = (page - 1) * size;
        rows.forEach((row) => { row.hidden = true; });
        matched.slice(first, first + size).forEach((row) => { row.hidden = false; });
        const scope = scopeOf(list);
        scope?.querySelector('[data-ta-no-results]')?.classList.toggle('d-none', matched.length !== 0 || rows.length === 0);
        const bar = scope?.querySelector(`[data-ta-pager-for="${CSS.escape(list.id)}"]`);
        if (!bar) return;
        bar.classList.add('ta-pager');
        const from = matched.length === 0 ? 0 : first + 1;
        const to = Math.min(first + size, matched.length);
        const unit = bar.dataset.taUnit || 'mục';
        const n = (value) => value.toLocaleString('vi-VN');
        let html = `<span class="ta-pager__info">Hiển thị <strong>${n(from)}–${n(to)}</strong> / ${n(matched.length)} ${unit}${matched.length !== rows.length ? ` (lọc từ ${n(rows.length)})` : ''}</span>`;
        if (state && rows.length > pageSizes[0]) {
            html += `<div class="ta-pager__sizes" role="group" aria-label="Số dòng mỗi trang">${pageSizes.map((value) => `<button type="button" data-ta-size="${value}" aria-pressed="${value === size}">${value}</button>`).join('')}</div>`;
        }
        if (state && pages > 1) {
            const numbers = [];
            for (let i = 1; i <= pages; i += 1) if (i === 1 || i === pages || Math.abs(i - page) <= 1) numbers.push(i);
            let last = 0;
            html += `<nav class="ta-pager__pages" aria-label="Phân trang"><button type="button" data-ta-page="${page - 1}" aria-label="Trang trước"${page === 1 ? ' disabled' : ''}>‹</button>`;
            numbers.forEach((value) => {
                if (value - last > 1) html += '<span class="ta-pager__gap" aria-hidden="true">…</span>';
                html += `<button type="button" data-ta-page="${value}"${value === page ? ' aria-current="page"' : ''} aria-label="Trang ${value}">${value}</button>`;
                last = value;
            });
            html += `<button type="button" data-ta-page="${page + 1}" aria-label="Trang sau"${page === pages ? ' disabled' : ''}>›</button></nav>`;
        }
        bar.innerHTML = html;
    };

    const setupLists = (root) => {
        root.querySelectorAll('[data-ta-pager]').forEach((list) => {
            if (!list.id) return;
            const preferred = storedSize(list.id);
            pagers.set(list.id, { page: 1, size: pageSizes.includes(preferred) ? preferred : Number(list.dataset.taPager) || pageSizes[0] });
            renderList(list);
        });
    };

    document.addEventListener('click', (event) => {
        const button = event.target.closest('[data-ta-pager-for] button');
        if (!button || button.disabled) return;
        const bar = button.closest('[data-ta-pager-for]');
        const list = document.getElementById(bar.dataset.taPagerFor);
        const state = list && pagers.get(list.id);
        if (!state) return;
        if (button.dataset.taSize) { state.size = Number(button.dataset.taSize); state.page = 1; storeSize(list.id, state.size); }
        else state.page = Number(button.dataset.taPage);
        list.classList.remove('ta-list-turn');
        void list.offsetWidth;
        list.classList.add('ta-list-turn');
        renderList(list);
        const scope = scopeOf(list);
        if (scope.getBoundingClientRect().top < 0) scope.scrollIntoView({ block: 'start', behavior: 'smooth' });
    });

    window.taFilterRows = (query, listId) => {
        const list = document.getElementById(listId);
        if (!list) return;
        const value = fold(query.trim());
        rowsOf(list).forEach((row) => { row.dataset.taFiltered = value !== '' && !fold(row.textContent).includes(value) ? '1' : '0'; });
        const state = pagers.get(listId);
        if (state) state.page = 1;
        renderList(list);
    };

    // ---- Toasts ----------------------------------------------------------------------------------------------------
    // Flash messages ([data-ta-toast], rendered in the page) move to a fixed stack at the top centre of the screen with a
    // close button; success hides itself after 5 s, an error after 9 s, paused while hovered or focused. Without script
    // they simply stay in the page.
    const toastStack = () => {
        let stack = document.querySelector('.ta-toasts');
        if (!stack) {
            stack = document.createElement('div');
            stack.className = 'ta-toasts';
            document.body.appendChild(stack);
        }
        return stack;
    };
    const hideToast = (toast) => {
        if (toast.classList.contains('is-leaving')) return;
        window.clearTimeout(toast.taTimer);
        toast.classList.add('is-leaving');
        window.setTimeout(() => toast.remove(), 220);
    };
    const raiseToasts = (root) => {
        root.querySelectorAll('[data-ta-toast]').forEach((toast) => {
            toast.removeAttribute('data-ta-toast');
            toast.classList.add('ta-toast');
            const close = document.createElement('button');
            close.type = 'button';
            close.className = 'ta-toast__close';
            close.setAttribute('aria-label', 'Đóng thông báo');
            close.textContent = '×';
            close.addEventListener('click', () => hideToast(toast));
            toast.appendChild(close);
            const delay = toast.getAttribute('role') === 'alert' ? 9000 : 5000;
            const arm = () => { window.clearTimeout(toast.taTimer); toast.taTimer = window.setTimeout(() => hideToast(toast), delay); };
            const hold = () => window.clearTimeout(toast.taTimer);
            toast.addEventListener('mouseenter', hold);
            toast.addEventListener('mouseleave', arm);
            toast.addEventListener('focusin', hold);
            toast.addEventListener('focusout', arm);
            toastStack().appendChild(toast);
            arm();
        });
    };
    document.addEventListener('keydown', (event) => {
        if (event.key === 'Escape') document.querySelectorAll('.ta-toasts .ta-toast').forEach(hideToast);
    });
    document.addEventListener('ta:load', () => raiseToasts(document));
    raiseToasts(document);

    // ---- Soft navigation -------------------------------------------------------------------------------------------
    // Only pages that share the same shell (elements with data-ta-swap and the same ids) are swapped in place; anything
    // else (login, logout, errors, other layouts) falls back to a normal browser navigation. Security is unchanged: the
    // same requests, cookies, antiforgery tokens and server-side authorization apply.
    const swapIds = () => Array.from(document.querySelectorAll('[data-ta-swap]')).map((element) => element.id).filter(Boolean);
    const prefetched = new Map();
    const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
    let navToken = 0;

    const eligibleLink = (link, event) => {
        if (!link || (link.target && link.target !== '_self') || link.hasAttribute('download') || link.hasAttribute('data-ta-hard') || link.matches('[data-bs-toggle]')) return false;
        if (event && (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey)) return false;
        const href = link.getAttribute('href') || '';
        if (href === '' || href.startsWith('#') || href.startsWith('mailto:') || href.startsWith('tel:')) return false;
        const url = new URL(link.href, window.location.href);
        return url.origin === window.location.origin && !(url.pathname === window.location.pathname && url.search === window.location.search && url.hash);
    };
    const request = (url, init) => fetch(url, { credentials: 'same-origin', headers: { 'X-TA-Soft': '1' }, ...init })
        .then(async (response) => ({ ok: response.ok || response.status === 400, status: response.status, url: response.url, html: await response.text() }));
    const prefetch = (url) => {
        const hit = prefetched.get(url);
        if (hit && Date.now() - hit.at < 15000) return hit.promise;
        const promise = request(url).catch(() => null);
        prefetched.set(url, { at: Date.now(), promise });
        return promise;
    };

    const swapIn = (doc, finalUrl, options) => {
        const ids = swapIds();
        if (ids.length === 0 || !ids.every((id) => doc.getElementById(id))) return false;
        const apply = () => {
            document.dispatchEvent(new CustomEvent('ta:before-swap'));
            document.title = doc.title;
            ids.forEach((id) => document.getElementById(id).replaceWith(document.importNode(doc.getElementById(id), true)));
            if (options.push) window.history.pushState({ ta: true }, '', finalUrl);
            if (!options.keepScroll) window.scrollTo(0, 0);
            document.querySelector('main[data-ta-swap]')?.focus({ preventScroll: true });
            setupLists(document);
            document.dispatchEvent(new CustomEvent('ta:load'));
        };
        if (document.startViewTransition && !reduceMotion.matches) document.startViewTransition(apply);
        else apply();
        return true;
    };

    const navigate = async (url, options) => {
        const token = ++navToken;
        const result = await (options.response || prefetch(url));
        if (token !== navToken) return;
        if (!result || !result.ok) { if (!window.taUpdate?.unavailable(result)) window.location.assign(result?.url || url); return; }
        const finalUrl = result.url || url;
        const doc = new DOMParser().parseFromString(result.html, 'text/html');
        if (!swapIn(doc, finalUrl, { ...options, push: options.push && finalUrl !== window.location.href || options.forcePush })) window.location.assign(finalUrl);
    };

    // ---- Sidebar follows the current page (operator 2026-11-13) --------------------------------------------------------
    // The menu keeps its scroll position across soft navigations and scrolls only when the current page's link is out of view.
    const navScroller = () => {
        let box = document.getElementById('taNav') || document.getElementById('cpNav');
        while (box && box !== document.body) {
            const overflow = window.getComputedStyle(box).overflowY;
            if ((overflow === 'auto' || overflow === 'scroll') && box.scrollHeight > box.clientHeight) return box;
            box = box.parentElement;
        }
        return null;
    };
    let navScrollTop = null;
    const revealCurrentNav = () => {
        const box = navScroller();
        if (!box) return;
        if (navScrollTop !== null) { box.scrollTop = navScrollTop; navScrollTop = null; }
        const link = box.querySelector('[aria-current="page"]');
        if (!link) return;
        const l = link.getBoundingClientRect(), b = box.getBoundingClientRect();
        if (l.top < b.top + 8 || l.bottom > b.bottom - 8) box.scrollTop += (l.top - b.top) - (b.height - l.height) / 2;
    };
    document.addEventListener('ta:before-swap', () => { navScrollTop = navScroller()?.scrollTop ?? null; });
    document.addEventListener('ta:load', revealCurrentNav);
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', revealCurrentNav); else revealCurrentNav();

    if (swapIds().length > 0 && window.fetch && window.DOMParser) {
        window.history.replaceState({ ta: true }, '', window.location.href);
        const warm = (event) => {
            const link = event.target.closest?.('a[href]');
            if (eligibleLink(link)) prefetch(new URL(link.href, window.location.href).href);
        };
        document.addEventListener('pointerover', warm, { passive: true });
        document.addEventListener('focusin', warm);
        document.addEventListener('touchstart', warm, { passive: true });
        document.addEventListener('click', (event) => {
            const link = event.target.closest('a[href]');
            if (event.defaultPrevented || !eligibleLink(link, event)) return;
            event.preventDefault();
            navigate(new URL(link.href, window.location.href).href, { push: true, keepScroll: link.hasAttribute('data-ta-keep-scroll') });
        });
        document.addEventListener('submit', (event) => {
            const form = event.target;
            const submitter = event.submitter;
            // A button's formaction / formmethod / formtarget override the form's (e.g. "Lấy mẫu hóa đơn" inside the settings form).
            const target = submitter?.hasAttribute('formtarget') ? submitter.getAttribute('formtarget') : form.target;
            if (event.defaultPrevented || !(form instanceof HTMLFormElement) || form.hasAttribute('data-ta-hard') || (target && target !== '_self')) return;
            event.preventDefault();
            if (form.dataset.taSubmitting === 'true') return;
            const data = new FormData(form, submitter);
            form.dataset.taSubmitting = 'true';
            const busy = window.setTimeout(() => submitter?.classList.add('is-busy'), 300);
            if (submitter) submitter.disabled = true;
            prefetched.clear();
            const action = new URL(submitter?.getAttribute('formaction') || form.getAttribute('action') || window.location.href, window.location.href);
            const isPost = (submitter?.getAttribute('formmethod') || form.getAttribute('method') || 'get').toLowerCase() === 'post';
            if (!isPost) action.search = new URLSearchParams(data).toString();
            const response = request(action.href, isPost ? { method: 'POST', body: data } : undefined).catch(() => null);
            response.then(() => {
                window.clearTimeout(busy);
                delete form.dataset.taSubmitting;
                if (submitter) { submitter.disabled = false; submitter.classList.remove('is-busy'); }
            });
            navigate(action.href, { push: true, forcePush: !isPost, response });
        });
        window.addEventListener('popstate', () => navigate(window.location.href, { push: false }));
    }

    setupLists(document);
})();
