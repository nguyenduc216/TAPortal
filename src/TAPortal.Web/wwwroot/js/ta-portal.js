(() => {
    'use strict';

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

    // Full-page submits only (login page): forms inside the shell are submitted in the background by ta-shell.js.
    document.addEventListener('submit', (event) => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement) || event.defaultPrevented) return;
        if (form.dataset.taSubmitting === 'true') {
            event.preventDefault();
            return;
        }
        form.dataset.taSubmitting = 'true';
        setButtonLoading(event.submitter || form.querySelector('[type="submit"]'));
    });

    // Bootstrap modals live at <body> level so no ancestor stacking context (page transition, sticky shell) can put
    // the modal under its own backdrop.
    document.addEventListener('hidden.bs.modal', (event) => {
        event.target.querySelectorAll('.password-rules li').forEach((item) => item.classList.remove('ok', 'fail'));
    });

    // Searchable dropdowns: every select.form-select gets a combobox with live search. The native <select> stays in
    // the form (hidden) and remains the submitted value, so model binding, onchange handlers and validation are unchanged.
    const fold = (text) => text.normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/đ/g, 'd').replace(/Đ/g, 'D').toLocaleLowerCase('vi');
    const iconHtml = (id) => document.getElementById(id)?.innerHTML ?? '';
    let selectSeq = 0;
    let openSelect = null;

    const enhanceSelect = (select) => {
        if (select.dataset.taEnhanced || select.multiple || select.hasAttribute('data-ta-native')) return;
        select.dataset.taEnhanced = 'true';
        const uid = `taSelect${++selectSeq}`;
        const small = select.classList.contains('form-select-sm');
        const wrap = document.createElement('div');
        wrap.className = 'ta-select' + (small ? ' ta-select--sm' : '');
        if (select.style.width) wrap.style.width = select.style.width;
        select.parentNode.insertBefore(wrap, select);
        wrap.appendChild(select);
        select.classList.add('ta-select__native');
        select.tabIndex = -1;
        select.setAttribute('aria-hidden', 'true');

        const trigger = document.createElement('button');
        trigger.type = 'button';
        trigger.id = `${uid}Trigger`;
        trigger.className = `form-select ta-select__trigger${small ? ' form-select-sm' : ''}`;
        trigger.setAttribute('aria-haspopup', 'listbox');
        trigger.setAttribute('aria-expanded', 'false');
        wrap.appendChild(trigger);
        const label = select.id ? document.querySelector(`label[for="${CSS.escape(select.id)}"]`) : null;
        if (label) label.htmlFor = trigger.id;
        const name = () => label?.textContent.trim() || select.getAttribute('aria-label') || '';

        const popup = document.createElement('div');
        popup.className = 'ta-select__popup';
        popup.hidden = true;
        popup.innerHTML = `<div class="ta-input-icon">${iconHtml('taIconSearch')}<input type="text" class="form-control form-control-sm" role="combobox" aria-autocomplete="list" aria-expanded="true" aria-controls="${uid}List" autocomplete="off" placeholder="Tìm kiếm..." aria-label="Tìm trong danh sách" /></div><ul class="ta-select__list" role="listbox" id="${uid}List"></ul><div class="ta-select__empty" hidden>Không tìm thấy kết quả</div>`;
        popup.dataset.taTransient = 'true';
        (select.closest('.modal') || document.body).appendChild(popup);
        const search = popup.querySelector('input');
        const list = popup.querySelector('ul');
        const empty = popup.querySelector('.ta-select__empty');
        let items = [];
        let active = -1;

        const sync = () => {
            const option = select.options[select.selectedIndex];
            const text = option ? option.text : '';
            trigger.textContent = text || ' ';
            trigger.disabled = select.disabled;
            trigger.setAttribute('aria-label', name() ? `${name()}: ${text}` : text);
        };
        const build = () => {
            list.innerHTML = '';
            items = [];
            Array.from(select.children).forEach((node) => {
                const options = node.tagName === 'OPTGROUP' ? Array.from(node.children) : [node];
                if (node.tagName === 'OPTGROUP') {
                    const group = document.createElement('li');
                    group.className = 'ta-select__group';
                    group.setAttribute('role', 'presentation');
                    group.textContent = node.label;
                    list.appendChild(group);
                }
                options.forEach((option) => {
                    const li = document.createElement('li');
                    li.id = `${uid}Opt${items.length}`;
                    li.className = 'ta-select__option';
                    li.setAttribute('role', 'option');
                    li.setAttribute('aria-selected', String(option.selected));
                    if (option.disabled) li.setAttribute('aria-disabled', 'true');
                    li.innerHTML = `<span class="ta-select__text"></span>${option.selected ? iconHtml('taIconCheck') : ''}`;
                    li.querySelector('.ta-select__text').textContent = option.text;
                    li.addEventListener('mousedown', (event) => event.preventDefault());
                    li.addEventListener('click', () => choose(option));
                    list.appendChild(li);
                    items.push({ li, option, key: fold(option.text) });
                });
            });
        };
        const visible = () => items.filter((item) => !item.li.hidden && !item.option.disabled);
        const setActive = (index) => {
            const pool = visible();
            items.forEach((item) => item.li.classList.remove('is-active'));
            active = pool.length === 0 ? -1 : Math.max(0, Math.min(index, pool.length - 1));
            if (active < 0) { search.removeAttribute('aria-activedescendant'); return; }
            pool[active].li.classList.add('is-active');
            search.setAttribute('aria-activedescendant', pool[active].li.id);
            pool[active].li.scrollIntoView({ block: 'nearest' });
        };
        const filter = () => {
            const query = fold(search.value.trim());
            items.forEach((item) => { item.li.hidden = query !== '' && !item.key.includes(query); });
            list.querySelectorAll('.ta-select__group').forEach((group) => {
                let next = group.nextElementSibling, any = false;
                while (next && !next.classList.contains('ta-select__group')) { if (!next.hidden) any = true; next = next.nextElementSibling; }
                group.hidden = !any;
            });
            empty.hidden = items.some((item) => !item.li.hidden);
            setActive(0);
        };
        const place = () => {
            const rect = trigger.getBoundingClientRect();
            popup.style.minWidth = `${Math.max(rect.width, 240)}px`;
            popup.style.left = `${Math.min(rect.left, window.innerWidth - Math.max(rect.width, 240) - 8)}px`;
            const below = window.innerHeight - rect.bottom;
            const height = popup.offsetHeight;
            popup.style.top = below < height + 8 && rect.top > below ? `${rect.top - height - 4}px` : `${rect.bottom + 4}px`;
        };
        const close = (focusTrigger) => {
            if (popup.hidden) return;
            popup.hidden = true;
            trigger.setAttribute('aria-expanded', 'false');
            if (openSelect === api) openSelect = null;
            if (focusTrigger) trigger.focus();
        };
        const open = () => {
            if (select.disabled) return;
            openSelect?.close(false);
            build();
            search.value = '';
            popup.hidden = false;
            filter();
            const selected = visible().findIndex((item) => item.option.selected);
            setActive(selected < 0 ? 0 : selected);
            place();
            trigger.setAttribute('aria-expanded', 'true');
            openSelect = api;
            search.focus();
        };
        const choose = (option) => {
            if (option.disabled) return;
            const changed = select.value !== option.value;
            select.value = option.value;
            trigger.classList.remove('is-invalid');
            wrap.querySelector('.ta-select__error')?.remove();
            sync();
            close(true);
            if (changed) select.dispatchEvent(new Event('change', { bubbles: true }));
        };
        const api = { close, place, popup, wrap };

        trigger.addEventListener('click', () => (popup.hidden ? open() : close(true)));
        trigger.addEventListener('keydown', (event) => {
            if (['ArrowDown', 'ArrowUp', 'Enter', ' '].includes(event.key)) { event.preventDefault(); open(); }
        });
        search.addEventListener('input', filter);
        search.addEventListener('keydown', (event) => {
            const pool = visible();
            if (event.key === 'ArrowDown') { event.preventDefault(); setActive(active + 1); }
            else if (event.key === 'ArrowUp') { event.preventDefault(); setActive(active - 1); }
            else if (event.key === 'Home') { event.preventDefault(); setActive(0); }
            else if (event.key === 'End') { event.preventDefault(); setActive(pool.length - 1); }
            else if (event.key === 'Enter') { event.preventDefault(); if (active >= 0) choose(pool[active].option); }
            else if (event.key === 'Escape') { event.preventDefault(); event.stopPropagation(); close(true); }
            else if (event.key === 'Tab') close(false);
        });
        // Required selects are hidden from the browser's own bubble: show the message on the visible control instead.
        select.addEventListener('invalid', (event) => {
            event.preventDefault();
            trigger.classList.add('is-invalid');
            if (!wrap.querySelector('.ta-select__error')) wrap.insertAdjacentHTML('beforeend', '<div class="invalid-feedback d-block ta-select__error">Vui lòng chọn một mục trong danh sách.</div>');
            trigger.focus();
        });
        select.addEventListener('change', sync);
        sync();
    };

    document.addEventListener('mousedown', (event) => {
        if (openSelect && !openSelect.popup.contains(event.target) && !openSelect.wrap.contains(event.target)) openSelect.close(false);
    });
    window.addEventListener('resize', () => openSelect?.place());
    document.addEventListener('scroll', (event) => {
        if (openSelect && !openSelect.popup.contains(event.target)) openSelect.place();
    }, true);
    document.addEventListener('hide.bs.modal', () => openSelect?.close(false));

    // Page initialisation: runs on first load and again after every soft navigation (ta:load from ta-shell.js).
    const init = () => {
        // Bootstrap modals live at <body> level so no ancestor stacking context can put a modal under its own backdrop.
        document.querySelectorAll('.ta-content .modal').forEach((modal) => {
            modal.dataset.taTransient = 'true';
            document.body.appendChild(modal);
        });
        document.querySelectorAll('select.form-select').forEach(enhanceSelect);
        // Reopen a modal re-rendered by the server after a validation error (keeps the entered values).
        document.querySelectorAll('.modal[data-ta-open="true"]').forEach((element) => {
            if (window.bootstrap?.Modal) window.bootstrap.Modal.getOrCreateInstance(element).show();
        });
        document.querySelectorAll('[data-bs-toggle="tooltip"]').forEach((element) => {
            if (window.bootstrap?.Tooltip) window.bootstrap.Tooltip.getOrCreateInstance(element);
        });
    };
    // Before content is swapped: close popups/modals of the old page and drop everything it moved to <body>.
    document.addEventListener('ta:before-swap', () => {
        openSelect?.close(false);
        document.querySelectorAll('.modal.show').forEach((modal) => window.bootstrap?.Modal.getInstance(modal)?.dispose());
        document.querySelectorAll('.modal-backdrop, [data-ta-transient]').forEach((element) => element.remove());
        document.body.classList.remove('modal-open');
        document.body.style.removeProperty('overflow');
        document.body.style.removeProperty('padding-right');
        document.querySelectorAll('.tooltip').forEach((element) => element.remove());
    });
    document.addEventListener('ta:load', init);
    init();
    window.addEventListener('pageshow', () => {
        document.querySelectorAll('form[data-ta-submitting="true"]').forEach((form) => delete form.dataset.taSubmitting);
        document.querySelectorAll('.ta-action-loading').forEach((button) => {
            if (button.dataset.taOriginalHtml) button.innerHTML = button.dataset.taOriginalHtml;
            button.disabled = false;
            button.removeAttribute('aria-busy');
            button.style.minWidth = '';
            button.classList.remove('ta-action-loading');
        });
    });
})();
