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
        const rows = [...document.querySelectorAll('#customersTable tbody tr[data-ta-row]')];
        const search = document.querySelector('[data-customer-search]');
        const info = document.querySelector('[data-customer-page-info]');
        const previous = document.querySelector('[data-page-prev]');
        const next = document.querySelector('[data-page-next]');
        let page = 1;
        const renderPage = () => {
            const query = search?.value.trim().toLocaleLowerCase() || '';
            const matched = rows.filter(row => row.textContent.toLocaleLowerCase().includes(query));
            const pages = Math.max(1, Math.ceil(matched.length / 10));
            page = Math.min(page, pages);
            rows.forEach(row => { row.hidden = true; });
            matched.slice((page - 1) * 10, page * 10).forEach(row => { row.hidden = false; });
            if (info) info.textContent = `${matched.length} khách hàng · Trang ${page}/${pages}`;
            if (previous) previous.disabled = page <= 1;
            if (next) next.disabled = page >= pages;
            document.querySelector('[data-ta-no-results]')?.classList.toggle('d-none', matched.length !== 0);
        };
        search?.addEventListener('input', () => { page = 1; renderPage(); });
        previous?.addEventListener('click', () => { page--; renderPage(); });
        next?.addEventListener('click', () => { page++; renderPage(); });
        renderPage();
        const dialog = document.getElementById('ta-customer-dialog');
        const form = document.querySelector('[data-customer-form]');
        const title = document.getElementById('customer-create-title');
        const contactList = form?.querySelector('[data-contact-list]');
        const selectTab = name => {
            form?.querySelectorAll('[data-customer-tab]').forEach(tab => { const active = tab.dataset.customerTab === name; tab.setAttribute('aria-selected', String(active)); tab.tabIndex = active ? 0 : -1; });
            form?.querySelectorAll('[data-customer-panel]').forEach(panel => { panel.hidden = panel.dataset.customerPanel !== name; });
        };
        form?.querySelectorAll('[data-customer-tab]').forEach(tab => tab.addEventListener('click', () => selectTab(tab.dataset.customerTab)));
        const updateContactEmpty = () => form?.querySelector('[data-contact-empty]')?.classList.toggle('d-none', contactList.children.length > 0);
        const addContact = contact => {
            if (!contactList || contactList.children.length >= 20) return;
            const index = contactList.children.length;
            const row = document.createElement('tr'); row.className = 'ta-contact-row';
            for (const [key, label, icon, type] of [['Name','Họ tên','user','text'],['Role','Chức vụ','briefcase','text'],['Email','Email','mail','email'],['Phone','Điện thoại','phone','tel']]) {
                const cell = document.createElement('td');
                const wrapper = document.createElement('span'); wrapper.className = 'input-icon';
                const addon = document.createElement('span'); addon.className = 'input-icon-addon';
                const symbol = document.createElement('i'); symbol.className = `ti ti-${icon}`; addon.append(symbol);
                const input = document.createElement('input'); input.className = 'form-control'; input.type = type; input.name = `Contacts[${index}].${key}`; input.value = contact?.[key.toLowerCase()] || ''; input.required = key === 'Name'; input.maxLength = key === 'Email' ? 256 : key === 'Name' ? 150 : key === 'Role' ? 100 : 50; input.setAttribute('aria-label', label);
                wrapper.append(addon, input); cell.append(wrapper); row.append(cell);
            }
            const actions = document.createElement('td'); const remove = document.createElement('button'); remove.type = 'button'; remove.className = 'btn btn-ta-secondary btn-sm'; remove.innerHTML = '<i class="ti ti-trash"></i> Xóa'; remove.setAttribute('aria-label', 'Xóa người liên hệ'); remove.onclick = () => { row.remove(); [...contactList.children].forEach((item, i) => item.querySelectorAll('input').forEach(input => input.name = input.name.replace(/Contacts\[\d+\]/, `Contacts[${i}]`))); updateContactEmpty(); }; actions.append(remove); row.append(actions); contactList.append(row); updateContactEmpty();
        };
        form?.querySelector('[data-add-contact]')?.addEventListener('click', () => addContact());
        document.querySelectorAll('[data-customer-edit]').forEach(button => button.addEventListener('click', () => {
            const customer = JSON.parse(document.getElementById(`customer-${button.dataset.customerEdit}`).textContent);
            form.reset(); contactList.replaceChildren();
            for (const key of ['Code','Name','TaxCode','Email','Phone','Address','LogoUrl','Status']) form.elements[key].value = customer[key[0].toLowerCase() + key.slice(1)] || '';
            customer.contacts.forEach(addContact);
            form.action = `/customers/update/${encodeURIComponent(customer.id)}`;
            title.textContent = 'Chỉnh sửa khách hàng'; selectTab('info'); open('ta-customer-dialog');
        }));
        document.querySelectorAll('[data-customer-delete]').forEach(button => button.addEventListener('click', async () => {
            if (!confirm(`Xóa khách hàng "${button.dataset.customerName}"? Dữ liệu sẽ được ẩn khỏi danh sách.`)) return;
            button.disabled = true;
            try {
                const token = form?.querySelector('[name="__RequestVerificationToken"]')?.value;
                const response = await fetch(`/customers/delete/${encodeURIComponent(button.dataset.customerDelete)}`, { method: 'POST', credentials: 'same-origin', headers: { 'RequestVerificationToken': token || '' } });
                const result = response.headers.get('content-type')?.includes('json') ? await response.json() : null;
                if (!response.ok || result?.success !== true) throw new Error(response.status === 403 ? 'Bạn không có quyền xóa khách hàng.' : result?.message || 'Không thể xóa khách hàng.');
                location.reload();
            } catch (failure) { alert(failure.message); button.disabled = false; }
        }));
        document.querySelector('[data-customer-create]')?.addEventListener('click', () => { form.reset(); contactList.replaceChildren(); updateContactEmpty(); selectTab('info'); form.action = '/customers/create'; title.textContent = 'Thêm khách hàng'; open('ta-customer-dialog'); });
        document.querySelectorAll('[data-customer-close]').forEach(button => button.addEventListener('click', () => {
            if (form?.dataset.saving === 'true') return;
            dialog.close(); form?.reset(); window.TAPortalRecovery?.clearDirty(form);
        }));
        form?.addEventListener('submit', async event => {
            event.preventDefault();
            if (form.dataset.saving === 'true') return;
            if (!form.checkValidity()) { const invalid = form.querySelector(':invalid'); selectTab(invalid?.closest('[data-customer-panel]')?.dataset.customerPanel || 'info'); invalid?.focus(); form.reportValidity(); return; }
            form.dataset.saving = 'true';
            const data = new FormData(form);
            const buttons = [...form.querySelectorAll('button')]; buttons.forEach(button => button.disabled = true);
            const submit = form.querySelector('[type=submit]'); submit.disabled = true;
            const error = form.querySelector('[data-customer-error]'); error.classList.add('d-none');
            try {
                const response = await fetch(form.action, { method: 'POST', body: data, credentials: 'same-origin' });
                const result = response.headers.get('content-type')?.includes('json') ? await response.json() : null;
                if (!response.ok || result?.success !== true) {
                    const messages = result?.errors ? Object.values(result.errors).flat().join(' ') : result?.message;
                    throw new Error(response.status === 403 ? 'Bạn không có quyền thực hiện thao tác này.' : messages || 'Không thể lưu khách hàng. Vui lòng kiểm tra thông tin và thử lại.');
                }
                window.TAPortalRecovery?.clearDirty(form); dialog.close(); location.reload();
            } catch (failure) { error.textContent = failure.message; error.classList.remove('d-none'); }
            finally { form.dataset.saving = 'false'; buttons.forEach(button => button.disabled = false); }
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
