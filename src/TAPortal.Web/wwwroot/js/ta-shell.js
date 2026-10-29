// T.A shell behaviour shared by TAPortal Admin and T.A Customer Portal (identical copy in both apps; a safe test compares them).
// Sidebar: desktop (>= 1200px) collapses to an icon rail and remembers it; tablet shows the rail and opens the full menu over
// the content; mobile (< 768px) uses an off-canvas drawer with overlay, close button and Escape. Also: password show/hide.
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
        syncToggles();
    }

    window.taTogglePassword = (inputId, button) => {
        const input = document.getElementById(inputId);
        if (!input) return;
        const show = input.type === 'password';
        input.type = show ? 'text' : 'password';
        button.setAttribute('aria-label', show ? 'Ẩn mật khẩu' : 'Hiện mật khẩu');
        button.setAttribute('aria-pressed', String(show));
        button.classList.toggle('is-shown', show);
    };
})();
