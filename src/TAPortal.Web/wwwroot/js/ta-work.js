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
        if (!(field instanceof HTMLSelectElement) || !field.hasAttribute('data-ta-autosubmit')) return;
        // Hoàn thành needs a note on the result (operator 2026-11-16): open the note box instead of submitting.
        const note = field.form?.querySelector('[data-ta-done-note]');
        if (note) {
            const box = note.querySelector('textarea');
            const done = field.value === 'DONE';
            note.hidden = !done;
            box.required = done;
            if (done) { box.focus(); return; }
            box.value = '';
        }
        field.form?.requestSubmit();
    });

    // ---- Danh mục công việc (operator 2026-11-16): add / edit in a popup (Thông tin chung + Checklist), quick edits on the grid
    // Checklist items are renumbered so the posted Input.Items[0..n] follows the order on screen.
    const renumberSteps = (list) => {
        const items = list.querySelectorAll('[data-ta-step]');
        items.forEach((li, i) => {
            li.querySelectorAll('[name^="Input.Items["]').forEach((f) => { f.name = f.name.replace(/Input\.Items\[[^\]]*\]/, `Input.Items[${i}]`); });
        });
        const root = list.closest('[data-ta-steps-root]');
        const dialog = list.closest('[data-ta-catalog-dialog]');
        root?.querySelector('[data-ta-steps-empty]')?.toggleAttribute('hidden', items.length > 0);
        dialog?.querySelectorAll('[data-ta-step-count]').forEach((c) => { c.textContent = String(items.length); });
    };
    const catalogDirty = (dialog, dirty) => {
        if (!dialog) return;
        dialog.classList.toggle('is-dirty', dirty);
        dialog.querySelectorAll('[data-ta-catalog-dirty]').forEach((m) => { m.hidden = !dirty; });
    };
    const catalogTab = (dialog, name) => {
        dialog.querySelectorAll('[data-ta-tab]').forEach((t) => {
            const on = t.dataset.taTab === name;
            t.setAttribute('aria-selected', on ? 'true' : 'false');
            t.tabIndex = on ? 0 : -1;
        });
        dialog.querySelectorAll('[data-ta-tab-panel]').forEach((p) => { p.hidden = p.dataset.taTabPanel !== name; });
    };
    // The popup's checklist as first loaded, so Hủy can put it back after items were added, moved or removed.
    const pristine = new WeakMap();
    const catalogOpen = (dialog, tab) => {
        const root = dialog.querySelector('[data-ta-steps-root]');
        if (root && !pristine.has(dialog)) pristine.set(dialog, root.innerHTML);
        catalogTab(dialog, tab || 'general');
        if (typeof dialog.showModal === 'function') dialog.showModal(); else dialog.setAttribute('open', '');
        const first = tab === 'checklist'
            ? dialog.querySelector('[data-ta-step] input') ?? dialog.querySelector('[data-ta-step-add]')
            : dialog.querySelector('input[name="Input.Name"]');
        first?.focus();
    };
    const catalogClose = (dialog, ask) => {
        if (ask && dialog.classList.contains('is-dirty') && !window.confirm('Bỏ các thay đổi chưa lưu?')) return;
        const root = dialog.querySelector('[data-ta-steps-root]');
        if (root && pristine.has(dialog)) root.innerHTML = pristine.get(dialog);
        dialog.querySelector('form')?.reset();
        const list = dialog.querySelector('[data-ta-steps]');
        if (list) renumberSteps(list);
        catalogDirty(dialog, false);
        if (typeof dialog.close === 'function') dialog.close(); else dialog.removeAttribute('open');
    };
    document.addEventListener('click', (event) => {
        const target = event.target instanceof Element ? event.target : null;
        if (!target) return;
        const open = target.closest('[data-ta-catalog-open]');
        if (open) {
            const dialog = document.getElementById(open.dataset.taCatalogOpen);
            if (dialog) catalogOpen(dialog, open.dataset.taCatalogTab);
            return;
        }
        const dialog = target.closest('[data-ta-catalog-dialog]');
        if (!dialog) return;
        const tab = target.closest('[data-ta-tab]');
        if (tab) { catalogTab(dialog, tab.dataset.taTab); return; }
        if (target.closest('[data-ta-catalog-cancel]')) { catalogClose(dialog, true); return; }
        const add = target.closest('[data-ta-step-add]');
        const remove = target.closest('[data-ta-step-remove]');
        const suggest = target.closest('[data-ta-step-suggest]');
        const root = target.closest('[data-ta-steps-root]');
        const list = root?.querySelector('[data-ta-steps]');
        if (!list) return;
        const newStep = () => {
            list.appendChild(root.querySelector('template[data-ta-step-template]').content.firstElementChild.cloneNode(true));
            return list.lastElementChild;
        };
        if (suggest) {
            // An item already used by the tasks of this name: appended to the template (filling an empty row first).
            const row = [...list.querySelectorAll('[data-ta-step]')].find((li) => li.querySelector('input').value.trim() === '') ?? newStep();
            row.querySelector('input').value = suggest.dataset.taStepSuggest;
            const used = suggest.closest('.ta-step__used');
            suggest.remove();
            if (used && !used.querySelector('[data-ta-step-suggest]')) used.remove();
            renumberSteps(list);
            catalogDirty(dialog, true);
            row.querySelector('textarea').focus();
        } else if (add) {
            const row = newStep();
            renumberSteps(list);
            catalogDirty(dialog, true);
            row.querySelector('input').focus();
        } else if (remove) {
            remove.closest('[data-ta-step]').remove();
            renumberSteps(list);
            catalogDirty(dialog, true);
        }
    });
    ['input', 'change'].forEach((type) => document.addEventListener(type, (event) => {
        const dialog = event.target?.closest?.('[data-ta-catalog-dialog]');
        if (dialog && event.target.matches('input, textarea, select')) catalogDirty(dialog, true);
    }));
    // Esc asks before throwing changes away, like Hủy; the backdrop does not close the popup.
    document.addEventListener('cancel', (event) => {
        if (!event.target?.matches?.('[data-ta-catalog-dialog]')) return;
        event.preventDefault();
        catalogClose(event.target, true);
    }, true);
    // A required field on the hidden tab: show that tab so the browser can point at it.
    document.addEventListener('invalid', (event) => {
        const panel = event.target?.closest?.('[data-ta-tab-panel]');
        const dialog = panel?.closest('[data-ta-catalog-dialog]');
        if (dialog && panel.hidden) catalogTab(dialog, panel.dataset.taTabPanel);
    }, true);
    document.addEventListener('keydown', (event) => {
        const tab = event.target?.closest?.('[data-ta-tab]');
        if (!tab || (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight')) return;
        const tabs = [...tab.parentElement.querySelectorAll('[data-ta-tab]')];
        const next = tabs[(tabs.indexOf(tab) + (event.key === 'ArrowRight' ? 1 : -1) + tabs.length) % tabs.length];
        catalogTab(tab.closest('[data-ta-catalog-dialog]'), next.dataset.taTab);
        next.focus();
    });
    document.addEventListener('submit', (event) => {
        const dialog = event.target?.closest?.('[data-ta-catalog-dialog]');
        if (dialog && !event.defaultPrevented) catalogDirty(dialog, false);
    });
    window.addEventListener('beforeunload', (event) => {
        if (document.querySelector('[data-ta-catalog-dialog][open].is-dirty')) { event.preventDefault(); event.returnValue = ''; }
    });
    document.addEventListener('ta:before-swap', () => document.querySelectorAll('[data-ta-catalog-dialog][open]').forEach((d) => d.close?.()));

    // Checklist order: drag an item by its handle (mouse or touch), or focus the handle and press ↑ / ↓.
    let dragging = null;
    document.addEventListener('pointerdown', (event) => {
        const handle = event.target?.closest?.('[data-ta-step-handle]');
        if (!handle || event.button > 0) return;
        const li = handle.closest('[data-ta-step]');
        dragging = { li, list: li.parentElement, moved: false };
        li.classList.add('is-dragging');
        handle.setPointerCapture?.(event.pointerId);
        event.preventDefault();
    });
    document.addEventListener('pointermove', (event) => {
        if (!dragging) return;
        const { li, list } = dragging;
        const others = [...list.querySelectorAll('[data-ta-step]')].filter((o) => o !== li);
        const before = others.find((o) => { const r = o.getBoundingClientRect(); return event.clientY < r.top + r.height / 2; }) ?? null;
        if (before ? li.nextElementSibling !== before : list.lastElementChild !== li) {
            list.insertBefore(li, before);
            dragging.moved = true;
        }
    });
    const dragEnd = () => {
        if (!dragging) return;
        const { li, list, moved } = dragging;
        dragging = null;
        li.classList.remove('is-dragging');
        if (moved) { renumberSteps(list); catalogDirty(list.closest('[data-ta-catalog-dialog]'), true); }
    };
    document.addEventListener('pointerup', dragEnd);
    document.addEventListener('pointercancel', dragEnd);
    document.addEventListener('keydown', (event) => {
        const handle = event.target?.closest?.('[data-ta-step-handle]');
        if (!handle || (event.key !== 'ArrowUp' && event.key !== 'ArrowDown')) return;
        event.preventDefault();
        const li = handle.closest('[data-ta-step]');
        const list = li.parentElement;
        if (event.key === 'ArrowUp' && li.previousElementSibling) list.insertBefore(li, li.previousElementSibling);
        else if (event.key === 'ArrowDown' && li.nextElementSibling) list.insertBefore(li.nextElementSibling, li);
        else return;
        renumberSteps(list);
        catalogDirty(list.closest('[data-ta-catalog-dialog]'), true);
        handle.focus();
    });

    // Quick edits on the grid: double-click (or Enter on) a name / description / order to edit it in place; Enter saves
    // through the entry's popup form, Esc puts the text back. Đang dùng saves as soon as it is switched.
    const inlineField = (cell) => document.getElementById(cell.dataset.taInlineForm)?.querySelector(`[name="${cell.dataset.taInline}"]`);
    const inlineStart = (cell) => {
        if (cell.querySelector('input')) return;
        const field = inlineField(cell);
        if (!field) return;
        const input = document.createElement('input');
        input.className = 'form-control form-control-sm ta-inline__input';
        input.type = cell.dataset.taInlineType || 'text';
        if (field.maxLength > 0) input.maxLength = field.maxLength;
        input.value = field.value;
        input.dataset.taInlineOld = cell.textContent;
        input.setAttribute('aria-label', 'Sửa nhanh: Enter để lưu, Esc để bỏ');
        cell.textContent = '';
        cell.appendChild(input);
        cell.classList.add('is-editing');
        input.focus();
        input.select();
    };
    const inlineStop = (cell, save) => {
        const input = cell.querySelector('input');
        if (!input) return;
        const field = inlineField(cell);
        const value = input.value.trim();
        const changed = field && value !== field.value.trim();
        if (save && changed && !(cell.hasAttribute('data-ta-inline-required') && value === '')) {
            field.value = value;
            input.disabled = true;
            cell.classList.add('is-saving');
            field.form.requestSubmit();
            return;
        }
        cell.textContent = input.dataset.taInlineOld;
        cell.classList.remove('is-editing');
    };
    document.addEventListener('dblclick', (event) => {
        const cell = event.target?.closest?.('[data-ta-inline]');
        if (cell) inlineStart(cell);
    });
    document.addEventListener('keydown', (event) => {
        const cell = event.target?.closest?.('[data-ta-inline]');
        if (!cell) return;
        const editing = event.target.matches('input');
        if (!editing && (event.key === 'Enter' || event.key === 'F2')) { event.preventDefault(); inlineStart(cell); }
        else if (editing && event.key === 'Enter') { event.preventDefault(); inlineStop(cell, true); }
        else if (editing && event.key === 'Escape') { event.preventDefault(); inlineStop(cell, false); cell.focus(); }
    });
    document.addEventListener('focusout', (event) => {
        const cell = event.target?.closest?.('[data-ta-inline]');
        if (cell && event.target.matches('input') && !event.target.disabled) inlineStop(cell, true);
    });
    document.addEventListener('change', (event) => {
        const box = event.target?.closest?.('[data-ta-inline-active]');
        if (!box) return;
        const form = document.getElementById(box.dataset.taInlineActive);
        const field = form?.querySelector('[data-ta-catalog-active]');
        if (!field) return;
        field.checked = box.checked;
        box.disabled = true;
        form.requestSubmit();
    });

    document.addEventListener('click', (event) => {
        const cancel = event.target instanceof Element ? event.target.closest('[data-ta-done-cancel]') : null;
        if (!cancel) return;
        const form = cancel.closest('form');
        const note = form.querySelector('[data-ta-done-note]');
        const select = form.querySelector('select[name="status"]');
        note.hidden = true;
        note.querySelector('textarea').required = false;
        select.value = select.querySelector('option[selected]')?.value ?? select.options[0].value;
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
    // ---- Tên công việc from Danh mục công việc (operator 2026-11-14) -------------------------------------------------
    // Typing shows the catalogue entries containing every typed word (accents optional); ↑/↓ + Enter or a click picks one.
    // A name outside the catalogue is refused before sending (the server checks too); an older task keeps its own name.
    const comboOf = (el) => el?.closest?.('[data-ta-combo]');
    const comboInput = (combo) => combo.querySelector('[data-ta-combo-input]');
    const comboOptions = (combo) => [...combo.querySelectorAll('.ta-combo__option')];
    const comboMatch = (combo, value) => comboOptions(combo).find((o) => fold(o.dataset.value) === fold((value || '').trim()));

    const comboShow = (combo, open) => {
        const list = combo.querySelector('.ta-combo__list');
        list.hidden = !open;
        comboInput(combo).setAttribute('aria-expanded', open ? 'true' : 'false');
    };

    const comboFilter = (combo) => {
        const words = fold(comboInput(combo).value).split(/\s+/).filter(Boolean);
        let shown = 0;
        comboOptions(combo).forEach((o) => {
            const text = fold(o.textContent);
            o.hidden = !words.every((w) => text.includes(w));
            o.classList.remove('is-active');
            o.setAttribute('aria-selected', 'false');
            if (!o.hidden) shown++;
        });
        combo.querySelector('[data-ta-combo-empty]').hidden = shown > 0;
        comboShow(combo, true);
    };

    const comboPick = (combo, option) => {
        const input = comboInput(combo);
        input.value = option.dataset.value;
        input.setCustomValidity('');
        // A search box (data-ta-combo-free) re-runs its own filter with the picked name; free text stays allowed.
        if (combo.hasAttribute('data-ta-combo-free')) input.dispatchEvent(new Event('input', { bubbles: true }));
        comboShow(combo, false);
        input.dispatchEvent(new Event('change', { bubbles: true }));
        // Checklist mẫu (operator 2026-11-16): fill the form's checklist with the entry's template, unless the user
        // already typed their own (a box still holding the previous template is replaced).
        const box = input.form?.querySelector('textarea[data-ta-catalog-checklist]');
        const template = option.dataset.checklist || '';
        if (box && (box.value.trim() === '' || box.value === box.dataset.taAutofill)) {
            box.value = template;
            box.dataset.taAutofill = template;
        }
    };

    const comboMove = (combo, step) => {
        const visible = comboOptions(combo).filter((o) => !o.hidden);
        if (visible.length === 0) return;
        const index = visible.findIndex((o) => o.classList.contains('is-active'));
        const next = visible[(index + step + visible.length) % visible.length];
        visible.forEach((o) => { o.classList.toggle('is-active', o === next); o.setAttribute('aria-selected', o === next ? 'true' : 'false'); });
        next.scrollIntoView({ block: 'nearest' });
    };

    // Accepted: an entry of the catalogue (written with its catalogue spelling) or the task's unchanged older name.
    const comboValid = (combo) => {
        const input = comboInput(combo);
        const match = comboMatch(combo, input.value);
        if (match) { input.value = match.dataset.value; input.setCustomValidity(''); return true; }
        if (input.value.trim() !== '' && input.value.trim() === (input.dataset.taComboOriginal || '').trim()) { input.setCustomValidity(''); return true; }
        input.setCustomValidity('Chọn tên công việc trong danh mục (gõ vài chữ rồi chọn đầu việc được gợi ý).');
        return false;
    };

    document.addEventListener('input', (event) => {
        const combo = comboOf(event.target);
        if (!combo || !event.target.matches('[data-ta-combo-input]')) return;
        event.target.setCustomValidity('');
        if (combo.hasAttribute('data-ta-combo-free') && event.target.value.trim() === '') comboShow(combo, false);
        else comboFilter(combo);
    });
    document.addEventListener('focusin', (event) => {
        const combo = comboOf(event.target);
        if (combo && event.target.matches('[data-ta-combo-input]') && !(combo.hasAttribute('data-ta-combo-free') && event.target.value.trim() === '')) comboFilter(combo);
    });
    document.addEventListener('focusout', (event) => {
        const combo = comboOf(event.target);
        if (!combo || combo.contains(event.relatedTarget)) return;
        comboShow(combo, false);
        if (!combo.hasAttribute('data-ta-combo-free') && comboInput(combo).value.trim() !== '') comboValid(combo);
    });
    // pointerdown, not click: the option is picked before the input loses focus and the list closes.
    document.addEventListener('pointerdown', (event) => {
        const option = event.target.closest?.('.ta-combo__option');
        const combo = comboOf(option);
        if (option && combo) { event.preventDefault(); comboPick(combo, option); }
    });
    document.addEventListener('keydown', (event) => {
        if (!event.target.matches?.('[data-ta-combo-input]')) return;
        const combo = comboOf(event.target);
        const open = !combo.querySelector('.ta-combo__list').hidden;
        if (event.key === 'ArrowDown' || event.key === 'ArrowUp') { event.preventDefault(); if (!open) comboFilter(combo); comboMove(combo, event.key === 'ArrowDown' ? 1 : -1); }
        else if (event.key === 'Enter' && open) {
            const active = combo.querySelector('.ta-combo__option.is-active') || comboOptions(combo).filter((o) => !o.hidden).at(0);
            if (active && !active.hidden) { event.preventDefault(); comboPick(combo, active); }
        }
        else if (event.key === 'Escape' && open) { event.preventDefault(); comboShow(combo, false); }
    });
    // Capture phase: runs before the shell's own submit handler, which skips a prevented submit.
    document.addEventListener('submit', (event) => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement)) return;
        for (const combo of form.querySelectorAll('[data-ta-combo]:not([data-ta-combo-free])')) {
            if (!comboValid(combo)) { event.preventDefault(); event.stopImmediatePropagation(); comboInput(combo).reportValidity(); comboInput(combo).focus(); return; }
        }
    }, true);
})();
