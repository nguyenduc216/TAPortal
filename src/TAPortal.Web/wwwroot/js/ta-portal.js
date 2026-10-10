(() => {
    'use strict';

    const progress = document.querySelector('[data-ta-progress]');
    let progressTimer;

    const startProgress = () => {
        if (!progress) return;
        clearTimeout(progressTimer);
        progress.classList.add('is-active');
        progress.setAttribute('aria-hidden', 'false');
    };

    const finishProgress = () => {
        if (!progress) return;
        progress.classList.add('is-complete');
        progressTimer = window.setTimeout(() => {
            progress.classList.remove('is-active', 'is-complete');
            progress.setAttribute('aria-hidden', 'true');
        }, 220);
    };

    const setButtonLoading = (button) => {
        if (!button || button.classList.contains('ta-action-loading')) return;
        const width = button.getBoundingClientRect().width;
        const loadingText = button.dataset.taLoadingText || 'Đang xử lý...';
        button.dataset.taOriginalHtml = button.innerHTML;
        if (width) button.style.minWidth = `${Math.ceil(width)}px`;
        button.disabled = true;
        button.setAttribute('aria-busy', 'true');
        button.classList.add('ta-action-loading');
        button.innerHTML = `<span class="spinner-border spinner-border-sm" aria-hidden="true"></span><span>${loadingText}</span>`;
    };

    document.addEventListener('submit', (event) => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement) || event.defaultPrevented) return;
        if (form.dataset.taSubmitting === 'true') {
            event.preventDefault();
            return;
        }
        form.dataset.taSubmitting = 'true';
        setButtonLoading(event.submitter || form.querySelector('[type="submit"]'));
        startProgress();
    });

    document.addEventListener('click', (event) => {
        const link = event.target.closest('a[href]');
        if (!link || event.defaultPrevented || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        const url = new URL(link.href, window.location.href);
        const staysOnPage = url.pathname === window.location.pathname && url.search === window.location.search && url.hash;
        const isPlaceholder = (link.getAttribute('href') || '').startsWith('#');
        if (url.origin === window.location.origin && !staysOnPage && !isPlaceholder && link.target !== '_blank' && !link.hasAttribute('download') && !link.matches('[data-bs-toggle]')) startProgress();
    });

    window.taFilterRows = (query, tableId) => {
        const table = document.getElementById(tableId);
        if (!table) return;
        const value = query.trim().toLocaleLowerCase('vi');
        let visible = 0;
        table.querySelectorAll('tbody tr[data-ta-row]').forEach((row) => {
            const matches = row.innerText.toLocaleLowerCase('vi').includes(value);
            row.hidden = !matches;
            if (matches) visible += 1;
        });
        table.closest('.ta-table-card')?.querySelector('[data-ta-no-results]')?.classList.toggle('d-none', visible !== 0 || value === '');
    };

    window.taCheckPassword = (value, rulesId) => {
        const box = document.getElementById(rulesId);
        if (!box) return;
        const rules = { len: value.length >= 8, letter: /[A-Za-z]/.test(value), digit: /\d/.test(value), special: /[^A-Za-z0-9]/.test(value) };
        Object.entries(rules).forEach(([name, valid]) => box.querySelector(`[data-rule="${name}"]`)?.classList.toggle('ok', valid));
    };

    window.taTogglePassword = (inputId, button) => {
        const input = document.getElementById(inputId);
        if (!input) return;
        const show = input.type === 'password';
        input.type = show ? 'text' : 'password';
        button.setAttribute('aria-label', show ? 'Ẩn mật khẩu' : 'Hiện mật khẩu');
        const icon = button.querySelector('i');
        if (icon) icon.className = show ? 'ti ti-eye-off' : 'ti ti-eye';
    };

    document.querySelectorAll('[data-bs-toggle="tooltip"]').forEach((element) => {
        if (window.bootstrap?.Tooltip) window.bootstrap.Tooltip.getOrCreateInstance(element);
    });
    document.querySelectorAll('.ta-field input').forEach((input) => {
        input.addEventListener('focus', () => input.closest('.ta-field')?.classList.add('is-focused'));
        input.addEventListener('blur', () => input.closest('.ta-field')?.classList.remove('is-focused'));
    });
    // FEATURE_ID: TAPORTAL-UI-PORTAL-SHELL
    // CHANGE_ID: TAPORTAL-20261010-UI-PORTAL-SHELL-001
    // Server markup owns route activation; JS owns persisted shell presentation only.
    const shell = document.querySelector('.ta-app-shell');
    const collapseKey = 'ta.portal.sidebar.collapsed';
    const setSidebarCollapsed = (collapsed) => {
        if (!shell) return;
        shell.classList.toggle('is-sidebar-collapsed', collapsed);
        const button = document.querySelector('[data-ta-sidebar-toggle]');
        button?.setAttribute('aria-pressed', collapsed ? 'true' : 'false');
        button?.setAttribute('aria-label', collapsed ? 'Mở rộng thanh điều hướng' : 'Thu gọn thanh điều hướng');
        const icon = button?.querySelector('i');
        if (icon) icon.className = collapsed ? 'ti ti-layout-sidebar-left-expand' : 'ti ti-layout-sidebar-left-collapse';
    };
    try { setSidebarCollapsed(localStorage.getItem(collapseKey) === 'true'); } catch { setSidebarCollapsed(false); }
    document.querySelector('[data-ta-sidebar-toggle]')?.addEventListener('click', () => {
        const collapsed = !shell?.classList.contains('is-sidebar-collapsed');
        setSidebarCollapsed(collapsed);
        try { localStorage.setItem(collapseKey, String(collapsed)); } catch { /* Storage may be unavailable. */ }
    });
    document.querySelectorAll('[data-ta-submenu-toggle]').forEach((toggle) => toggle.addEventListener('click', () => {
        const node = toggle.closest('[data-ta-nav-node]');
        if (!node) return;
        const open = node.classList.toggle('is-open');
        toggle.setAttribute('aria-expanded', String(open));
    }));
    const setMobileSidebar = (open) => shell?.classList.toggle('is-mobile-nav-open', open);
    document.querySelector('[data-ta-sidebar-open]')?.addEventListener('click', () => setMobileSidebar(true));
    document.querySelectorAll('[data-ta-sidebar-close]').forEach((button) => button.addEventListener('click', () => setMobileSidebar(false)));
    document.addEventListener('keydown', (event) => { if (event.key === 'Escape') setMobileSidebar(false); });

    window.addEventListener('pageshow', () => {
        document.querySelectorAll('form[data-ta-submitting="true"]').forEach((form) => delete form.dataset.taSubmitting);
        document.querySelectorAll('.ta-action-loading').forEach((button) => {
            if (button.dataset.taOriginalHtml) button.innerHTML = button.dataset.taOriginalHtml;
            button.disabled = false;
            button.removeAttribute('aria-busy');
            button.style.minWidth = '';
            button.classList.remove('ta-action-loading');
        });
        finishProgress();
    });
})();
