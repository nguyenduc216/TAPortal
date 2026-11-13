// Admin → Công việc, and speech-to-text for text fields. Loaded on every page (the shell swaps <main> without running
// page scripts), so everything works through delegated listeners and the ta:load event.
(() => {
    'use strict';

    // ---- Quick updates on a task card ("Việc của tôi") -------------------------------------------------------------
    // A form marked data-ta-card-form inside [data-ta-task] posts with fetch; the server answers with the re-rendered card
    // (header X-TA-Card), which replaces the old one in place: no page reload, no scroll jump. Capture phase, so this runs
    // before the shell's own submit handler, which then sees defaultPrevented and leaves the form alone.
    document.addEventListener('submit', async (event) => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement) || !form.hasAttribute('data-ta-card-form') || !window.fetch) return;
        const card = form.closest('[data-ta-task]');
        if (!card) return;
        event.preventDefault();
        if (form.dataset.taBusy === 'true') return;
        form.dataset.taBusy = 'true';
        card.classList.add('is-saving');
        const focusName = document.activeElement && form.contains(document.activeElement) ? document.activeElement.name : null;
        try {
            const response = await fetch(form.action, { method: 'POST', body: new FormData(form, event.submitter), credentials: 'same-origin', headers: { 'X-TA-Card': '1' } });
            const html = await response.text();
            if (!response.ok) { showError(card, response.status === 404 || response.status === 403 ? html : 'Không lưu được, vui lòng thử lại.'); return; }
            const template = document.createElement('template');
            template.innerHTML = html.trim();
            const next = template.content.firstElementChild;
            if (!next) return;
            card.replaceWith(next);
            scanVoice(next);
            // Keep typing flow: refocus the same kind of field (e.g. the next checklist item) after a quick add.
            if (focusName === 'title' || focusName === 'body') next.querySelector(`[name="${focusName}"]`)?.focus();
        } catch {
            showError(card, 'Không kết nối được máy chủ, vui lòng thử lại.');
        } finally {
            delete form.dataset.taBusy;
            card.classList.remove('is-saving');
        }
    }, true);

    const showError = (card, text) => {
        let box = card.querySelector('[data-ta-card-error]');
        if (!box) {
            box = document.createElement('div');
            box.className = 'ta-alert ta-alert--danger ta-task__error';
            box.setAttribute('role', 'alert');
            box.setAttribute('data-ta-card-error', '');
            card.querySelector('.ta-task__head')?.after(box);
        }
        box.textContent = text;
    };

    // Status selects submit on change (a <noscript> button covers browsers without JavaScript).
    document.addEventListener('change', (event) => {
        const field = event.target;
        if (field instanceof HTMLSelectElement && field.hasAttribute('data-ta-autosubmit')) field.form?.requestSubmit();
    });

    // ---- Employee picker (Views/Work/_PeoplePicker) -------------------------------------------------------------------
    // Search box filters the tiles; the chosen owner is shown (and not tickable) among the co-workers.
    document.addEventListener('input', (event) => {
        const field = event.target;
        if (!(field instanceof HTMLInputElement) || !field.hasAttribute('data-ta-people-filter')) return;
        const picker = field.closest('[data-ta-people]');
        const q = field.value.trim().toLowerCase();
        let shown = 0;
        picker.querySelectorAll('[data-ta-person]').forEach((tile) => {
            const hit = q === '' || tile.dataset.taSearch.includes(q);
            tile.hidden = !hit;
            if (hit) shown++;
        });
        const empty = picker.querySelector('.ta-people__empty');
        if (empty) empty.hidden = shown > 0;
    });

    const syncOwner = (form) => {
        const owner = form?.querySelector('[data-ta-people="owner"] .ta-person__input:checked')?.value;
        form?.querySelectorAll('[data-ta-people="members"] [data-ta-person]').forEach((tile) => {
            const box = tile.querySelector('.ta-person__input');
            const isOwner = tile.dataset.taPerson === owner;
            tile.classList.toggle('is-owner', isOwner);
            if (isOwner) box.checked = false;
            box.disabled = isOwner;
        });
    };
    document.addEventListener('change', (event) => {
        const field = event.target;
        if (field instanceof HTMLInputElement && field.closest('[data-ta-people="owner"]')) syncOwner(field.form);
    });
    const scanPeople = (root) => root?.querySelectorAll?.('[data-ta-people="owner"]').forEach((p) => syncOwner(p.closest('form')));
    document.addEventListener('ta:load', () => scanPeople(document));
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', () => scanPeople(document));
    else scanPeople(document);

    // Enter in a one-line comment box sends; Shift+Enter keeps a new line.
    document.addEventListener('keydown', (event) => {
        const field = event.target;
        if (event.key === 'Enter' && !event.shiftKey && !event.isComposing && field instanceof HTMLTextAreaElement && field.closest('[data-ta-task]') && field.value.trim() !== '') {
            event.preventDefault();
            field.form?.requestSubmit();
        }
    });

    // ---- Speech-to-text ("nút micro") ---------------------------------------------------------------------------------
    // Every <textarea> and every input marked data-ta-voice gets a microphone button (data-ta-voice="off" opts out). Uses the
    // browser's Web Speech API in Vietnamese (Chrome / Edge / Safari); where it is missing no button is shown. The text is
    // appended to what the field already holds and stays editable before saving.
    const Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;
    const micIcon = '<svg class="ta-icon" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><path d="M9 2m0 3a3 3 0 0 1 3 -3h0a3 3 0 0 1 3 3v5a3 3 0 0 1 -3 3h0a3 3 0 0 1 -3 -3z"/><path d="M5 10a7 7 0 0 0 14 0"/><path d="M8 21l8 0"/><path d="M12 17l0 4"/></svg>';
    let active = null;

    const join = (before, spoken) => {
        const text = spoken.trim();
        if (!text) return before;
        return before && !/\s$/.test(before) ? `${before} ${text}` : `${before}${text}`;
    };

    const stop = () => { if (active) { try { active.recognition.stop(); } catch { /* already stopped */ } } };

    const listen = (field, button) => {
        const same = active && active.field === field;
        stop();
        if (same) return;
        const recognition = new Recognition();
        recognition.lang = 'vi-VN';
        recognition.interimResults = true;
        recognition.continuous = true;
        const before = field.value;
        let finalText = '';
        recognition.onresult = (event) => {
            let interim = '';
            for (let i = event.resultIndex; i < event.results.length; i++) {
                const result = event.results[i];
                if (result.isFinal) finalText += result[0].transcript + ' ';
                else interim += result[0].transcript;
            }
            const max = field.maxLength > 0 ? field.maxLength : Infinity;
            field.value = join(before, finalText + interim).slice(0, max);
            field.dispatchEvent(new Event('input', { bubbles: true }));
        };
        recognition.onerror = (event) => {
            if (event.error === 'not-allowed' || event.error === 'service-not-allowed') {
                button.title = 'Trình duyệt chưa cho phép dùng micro cho trang này.';
                button.classList.add('is-denied');
            }
        };
        recognition.onend = () => {
            button.classList.remove('is-listening');
            button.setAttribute('aria-pressed', 'false');
            if (active && active.recognition === recognition) active = null;
        };
        try { recognition.start(); } catch { return; }
        active = { recognition, field };
        button.classList.add('is-listening');
        button.setAttribute('aria-pressed', 'true');
        field.focus();
    };

    const scanVoice = (root) => {
        if (!Recognition || !root?.querySelectorAll) return;
        root.querySelectorAll('textarea:not([data-ta-voice="off"]), input[data-ta-voice]:not([data-ta-voice="off"])').forEach((field) => {
            if (field.dataset.taVoiceReady || field.readOnly || field.disabled || field.type === 'hidden') return;
            field.dataset.taVoiceReady = 'true';
            // Inside the design system's left-icon wrapper the button joins that wrapper; otherwise the field gets its own.
            let wrap = field.parentElement;
            if (wrap?.classList.contains('ta-input-icon')) wrap.classList.add('has-voice');
            else {
                wrap = document.createElement('span');
                wrap.className = 'ta-voice';
                field.parentNode.insertBefore(wrap, field);
                wrap.appendChild(field);
            }
            if (field.tagName === 'TEXTAREA') wrap.classList.add('ta-voice--area');
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'ta-voice__btn';
            button.setAttribute('aria-pressed', 'false');
            button.setAttribute('aria-label', 'Nhập bằng giọng nói');
            button.title = 'Nhập bằng giọng nói (bấm lần nữa để dừng)';
            button.innerHTML = micIcon;
            button.addEventListener('click', () => listen(field, button));
            wrap.appendChild(button);
        });
    };

    window.taVoice = { scan: scanVoice, stop };
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', () => scanVoice(document));
    else scanVoice(document);
    document.addEventListener('ta:load', () => scanVoice(document));
    document.addEventListener('ta:before-swap', stop);
    // ---- Task list filter popup (Danh sách công việc) ---------------------------------------------------------------
    // "Bộ lọc" opens a <dialog> inside the search form. Choosing employees narrows the customer list to the customers of
    // those employees (manager / executor / task / contract, from the JSON the page embeds); the search boxes filter the
    // employee and customer lists as you type. "Áp dụng" submits the form (GET), so the shell loads the filtered list.
    const WHY = { MANAGER: 'Quản lý', EXECUTOR: 'Thực hiện', TASK: 'Có việc', CONTRACT: 'Hợp đồng' };
    const fold = (text) => (text || '').normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/đ/g, 'd').replace(/Đ/g, 'D').toLowerCase();

    const filterCustomers = (dialog) => {
        let links = {};
        try { links = JSON.parse(dialog.querySelector('[data-ta-filter-links]')?.textContent || '{}'); } catch { links = {}; }
        const chosen = [...dialog.querySelectorAll('[data-ta-filter-employee]:checked')].map((box) => box.value.toLowerCase());
        const showAll = dialog.dataset.taAllCustomers === 'true';
        const narrow = chosen.length > 0 && !showAll;
        const why = {};
        for (const id of chosen) {
            for (const [customer, sources] of Object.entries(links[id] || {})) {
                const set = why[customer.toLowerCase()] ||= new Set();
                sources.forEach((s) => set.add(s));
            }
        }
        const query = fold(dialog.querySelector('[data-ta-filter-find="customers"]')?.value);
        let visible = 0;
        dialog.querySelectorAll('[data-ta-customer]').forEach((row) => {
            const id = row.dataset.taCustomer.toLowerCase();
            const box = row.querySelector('input');
            const reasons = why[id] ? [...why[id]].map((s) => WHY[s] || s).join(', ') : '';
            row.querySelector('[data-ta-why]').textContent = chosen.length > 0 ? reasons : '';
            const match = (!narrow || why[id] || box.checked) && (!query || fold(row.textContent).includes(query));
            row.hidden = !match;
            if (match) visible++;
        });
        const hint = dialog.querySelector('[data-ta-filter-customer-hint]');
        if (hint) hint.hidden = !narrow;
        const none = dialog.querySelector('[data-ta-filter-none]');
        if (none) none.hidden = visible > 0;
    };

    const filterEmployees = (dialog) => {
        const query = fold(dialog.querySelector('[data-ta-filter-find="employees"]')?.value);
        dialog.querySelectorAll('[data-ta-filter-list="employees"] .ta-filter-option').forEach((row) => {
            row.hidden = !!query && !fold(row.textContent).includes(query) && !row.querySelector('input').checked;
        });
    };

    document.addEventListener('click', (event) => {
        const open = event.target.closest('[data-ta-filter-open]');
        if (open) {
            const dialog = open.closest('form')?.querySelector('[data-ta-filter-dialog]');
            if (!dialog) return;
            dialog.dataset.taAllCustomers = 'false';
            filterCustomers(dialog);
            if (typeof dialog.showModal === 'function') dialog.showModal(); else dialog.setAttribute('open', '');
            return;
        }
        const dialog = event.target.closest('[data-ta-filter-dialog]');
        if (!dialog) return;
        if (event.target === dialog || event.target.closest('[data-ta-filter-close]')) { dialog.close?.(); return; }
        if (event.target.closest('[data-ta-filter-all-customers]')) { dialog.dataset.taAllCustomers = 'true'; filterCustomers(dialog); return; }
        if (event.target.closest('[data-ta-filter-reset]')) {
            dialog.querySelectorAll('input[type=checkbox]').forEach((box) => { box.checked = false; });
            dialog.querySelectorAll('input[type=date], input[type=search]').forEach((input) => { input.value = ''; });
            dialog.dataset.taAllCustomers = 'false';
            filterEmployees(dialog);
            filterCustomers(dialog);
        }
    });

    document.addEventListener('change', (event) => {
        const dialog = event.target.closest?.('[data-ta-filter-dialog]');
        if (dialog && event.target.matches('[data-ta-filter-employee]')) { dialog.dataset.taAllCustomers = 'false'; filterCustomers(dialog); }
    });

    document.addEventListener('input', (event) => {
        const dialog = event.target.closest?.('[data-ta-filter-dialog]');
        if (!dialog || !event.target.matches('[data-ta-filter-find]')) return;
        if (event.target.dataset.taFilterFind === 'employees') filterEmployees(dialog); else filterCustomers(dialog);
    });

    // Enter in a popup search box filters the list instead of submitting the whole form.
    document.addEventListener('keydown', (event) => {
        if (event.key === 'Enter' && event.target.matches?.('[data-ta-filter-find]')) event.preventDefault();
    });

    // Submitting closes the popup first (the popup's own search boxes have no name, so they are not sent).
    document.addEventListener('submit', (event) => {
        const form = event.target;
        if (form instanceof HTMLFormElement && form.hasAttribute('data-ta-task-filter')) form.querySelector('[data-ta-filter-dialog]')?.close?.();
    }, true);
    document.addEventListener('ta:before-swap', () => document.querySelectorAll('[data-ta-filter-dialog][open]').forEach((d) => d.close?.()));
})();
