(() => {
    'use strict';
    if (window.taCustomers) return;
    // CHANGE_ID: TAPORTAL-20261012-CUSTOMERS-001 — focus and keyboard containment use the native dialog; Escape is an explicit cancel.
    function open(id) {
        const dialog = document.getElementById(id);
        if (!dialog) return;
        if (!dialog.open) dialog.showModal();
        dialog.oncancel = event => {
            event.preventDefault();
            const close = dialog.querySelector('[aria-label="Đóng"]');
            if (close && !close.disabled) close.click();
        };
    }
    // CHANGE_ID: TAPORTAL-20261012-CUSTOMERS-001 — validate the server response before creating a local workbook download.
    async function exportCustomers(search) {
        const response = await fetch(`/customers/export?search=${encodeURIComponent(search || '')}`, { credentials: 'same-origin', cache: 'no-store' });
        if (!response.ok || !response.headers.get('content-type')?.includes('spreadsheetml.sheet')) throw new Error('Không thể xuất dữ liệu. Vui lòng kiểm tra quyền truy cập và thử lại.');
        const blob = await response.blob();
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement('a'); anchor.href = url;
        const date = new Intl.DateTimeFormat('sv-SE', { timeZone: 'Asia/Bangkok' }).format(new Date()).replaceAll('-', '');
        anchor.download = `Khach-hang-${date}.xlsx`; document.body.append(anchor); anchor.click(); anchor.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    }
    // CHANGE_ID: TAPORTAL-20261012-CUSTOMERS-001 — MVC uses the same server DTO; failures retain values and duplicate submits are blocked before awaits.
    function init() {
        const dialog = document.getElementById('ta-customer-dialog');
        const form = document.querySelector('[data-customer-form]');
        document.querySelector('[data-customer-create]')?.addEventListener('click', () => open('ta-customer-dialog'));
        document.querySelectorAll('[data-customer-close]').forEach(button => button.addEventListener('click', () => {
            if (form?.dataset.saving === 'true') return;
            dialog.close(); form?.reset(); window.TAPortalRecovery?.clearDirty(form);
        }));
        form?.addEventListener('submit', async event => {
            event.preventDefault();
            if (form.dataset.saving === 'true' || !form.reportValidity()) return;
            form.dataset.saving = 'true';
            const data = new FormData(form);
            const buttons = [...form.querySelectorAll('button')]; buttons.forEach(button => button.disabled = true);
            const submit = form.querySelector('[type=submit]'); submit.textContent = 'Đang lưu...';
            const error = form.querySelector('[data-customer-error]'); error.classList.add('d-none');
            try {
                const response = await fetch(form.action, { method: 'POST', body: data, credentials: 'same-origin' });
                const result = response.headers.get('content-type')?.includes('json') ? await response.json() : null;
                if (!response.ok || result?.success !== true) {
                    const messages = result?.errors ? Object.values(result.errors).flat().join(' ') : result?.message;
                    throw new Error(messages || 'Không thể lưu khách hàng. Vui lòng kiểm tra quyền truy cập và thử lại.');
                }
                window.TAPortalRecovery?.clearDirty(form); dialog.close(); location.reload();
            } catch (failure) { error.textContent = failure.message; error.classList.remove('d-none'); }
            finally { form.dataset.saving = 'false'; buttons.forEach(button => button.disabled = false); submit.textContent = 'Lưu khách hàng'; }
        });
        const button = document.querySelector('[data-customer-export]');
        button?.addEventListener('click', async () => {
            if (button.disabled) return; button.disabled = true;
            const feedback = document.querySelector('[data-customer-feedback]'); feedback?.classList.add('d-none');
            try { await exportCustomers(document.querySelector('[data-customer-search]')?.value); }
            catch (failure) { if (feedback) { feedback.textContent = failure.message; feedback.classList.remove('d-none'); } }
            finally { button.disabled = false; }
        });
    }
    window.taCustomers = { open, export: exportCustomers };
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init, { once: true }); else init();
})();
