// Admin → Website TA editors: repeatable section items, image picker (library + upload), section order on a page, the
// article rich-text editor and character counters. Loaded on every page (the shell swaps <main> without running page
// scripts), so everything uses delegated listeners and initialises on DOMContentLoaded and ta:load.
(() => {
    'use strict';
    const MEDIA_ADMIN = '/WebsiteMedia/File/';
    const MEDIA_PUBLIC = '/media/';

    const token = () => document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';

    // ---- Repeatable list items (section editor) --------------------------------------------------------------------
    // Names look like c.items[3].title; nested lists are not used. Items are renumbered 0..n-1 in DOM order.
    const renumber = (list) => {
        const base = list.dataset.taWebList;
        const items = [...list.querySelector('.ta-web-list__items').children];
        items.forEach((item, i) => {
            item.querySelector('.ta-web-item__no').textContent = '#' + (i + 1);
            item.querySelectorAll('[name]').forEach((el) => {
                if (el.name.startsWith(base + '[')) el.name = el.name.replace(/^([^\[]+)\[(\d+|__i__)\]/, `$1[${i}]`);
            });
            item.querySelectorAll('[id]').forEach((el) => {
                const label = item.querySelector(`label[for="${el.id}"]`);
                const id = el.name ? el.name.replace(/[.\[\]]/g, '_') : el.id;
                if (label) label.htmlFor = id;
                el.id = id;
            });
        });
        const count = list.querySelector('.ta-web-list__head small');
        if (count) count.textContent = `${items.length} / tối đa ${list.dataset.max} mục`;
        const add = list.querySelector('[data-ta-item="add"]');
        if (add) add.disabled = items.length >= Number(list.dataset.max || 99);
    };

    document.addEventListener('click', (event) => {
        const button = event.target.closest('[data-ta-item]');
        if (!button) return;
        const list = button.closest('[data-ta-web-list]');
        if (!list) return;
        const container = list.querySelector('.ta-web-list__items');
        const item = button.closest('[data-ta-web-item]');
        const action = button.dataset.taItem;
        if (action === 'add') {
            const template = list.querySelector('template[data-ta-web-template]');
            const node = template.content.firstElementChild.cloneNode(true);
            container.appendChild(node);
            renumber(list);
            node.querySelector('input,textarea')?.focus();
        } else if (action === 'remove' && item) {
            const filled = [...item.querySelectorAll('input:not([type=hidden]),textarea')].some((el) => el.value.trim());
            if (filled && !window.confirm('Xóa mục này?')) return;
            item.remove();
            renumber(list);
        } else if (action === 'copy' && item) {
            const clone = item.cloneNode(true);
            item.querySelectorAll('textarea').forEach((t, i) => { clone.querySelectorAll('textarea')[i].value = t.value; });
            item.after(clone);
            renumber(list);
        } else if (action === 'up' && item?.previousElementSibling) {
            item.previousElementSibling.before(item);
            renumber(list);
        } else if (action === 'down' && item?.nextElementSibling) {
            item.nextElementSibling.after(item);
            renumber(list);
        }
    });

    // ---- Section order on a page -----------------------------------------------------------------------------------
    document.addEventListener('click', (event) => {
        const button = event.target.closest('[data-ta-move]');
        if (!button) return;
        const row = button.closest('.ta-web-section');
        if (!row) return;
        if (button.dataset.taMove === '-1' && row.previousElementSibling) row.previousElementSibling.before(row);
        if (button.dataset.taMove === '1' && row.nextElementSibling) row.nextElementSibling.after(row);
        row.closest('form')?.classList.add('is-dirty');
        button.focus();
    });
    document.addEventListener('change', (event) => {
        const box = event.target.closest('[data-ta-web-visible]');
        if (box) box.closest('.ta-web-section')?.classList.toggle('is-hidden', !box.checked);
    });

    // ---- Image picker -------------------------------------------------------------------------------------------------
    let dialog = null;
    let onPick = null;

    const ensureDialog = () => {
        if (dialog && document.body.contains(dialog)) return dialog;
        dialog = document.createElement('dialog');
        dialog.className = 'ta-web-picker';
        dialog.innerHTML = `
            <form method="dialog" class="ta-web-picker__head"><b>Chọn ảnh</b><button class="btn btn-sm" value="close" aria-label="Đóng">✕</button></form>
            <div class="ta-web-picker__tools">
                <input class="form-control" type="search" placeholder="Tìm ảnh..." data-picker-search />
                <label class="btn btn-ta-gold mb-0">Tải ảnh mới<input type="file" accept="image/png,image/jpeg,image/webp,image/gif" hidden data-picker-upload /></label>
            </div>
            <p class="ta-web-picker__msg" data-picker-msg></p>
            <div class="ta-web-picker__grid" data-picker-grid></div>`;
        document.body.appendChild(dialog);
        let timer = 0;
        dialog.querySelector('[data-picker-search]').addEventListener('input', (e) => {
            window.clearTimeout(timer);
            timer = window.setTimeout(() => load(e.target.value), 250);
        });
        dialog.querySelector('[data-picker-upload]').addEventListener('change', async (e) => {
            const file = e.target.files[0];
            if (!file) return;
            const msg = dialog.querySelector('[data-picker-msg]');
            if (file.size > 5 * 1024 * 1024) { msg.textContent = 'Ảnh tối đa 5 MB. Hãy nén ảnh rồi tải lại.'; return; }
            msg.textContent = 'Đang tải ảnh lên...';
            const body = new FormData();
            body.append('file', file);
            body.append('__RequestVerificationToken', token());
            try {
                const response = await fetch('/WebsiteMedia/Upload', { method: 'POST', body, credentials: 'same-origin', headers: { Accept: 'application/json' } });
                const data = await response.json().catch(() => ({}));
                if (!response.ok) { msg.textContent = data.error || 'Không tải được ảnh (thiếu quyền hoặc lỗi máy chủ).'; return; }
                msg.textContent = '';
                choose(data);
            } catch { msg.textContent = 'Không kết nối được máy chủ.'; }
            e.target.value = '';
        });
        dialog.addEventListener('click', (e) => {
            const tile = e.target.closest('[data-picker-item]');
            if (tile) choose(JSON.parse(tile.dataset.pickerItem));
        });
        return dialog;
    };

    const choose = (item) => {
        dialog.close();
        if (onPick) onPick(item);
    };

    const load = async (q) => {
        const grid = dialog.querySelector('[data-picker-grid]');
        const msg = dialog.querySelector('[data-picker-msg]');
        grid.innerHTML = '';
        msg.textContent = 'Đang tải...';
        try {
            const response = await fetch('/WebsiteMedia/Picker?q=' + encodeURIComponent(q || ''), { credentials: 'same-origin', headers: { Accept: 'application/json' } });
            if (!response.ok) { msg.textContent = 'Không đọc được thư viện ảnh.'; return; }
            const items = await response.json();
            msg.textContent = items.length ? '' : 'Thư viện chưa có ảnh. Bấm "Tải ảnh mới".';
            for (const item of items) {
                const tile = document.createElement('button');
                tile.type = 'button';
                tile.className = 'ta-web-picker__item';
                tile.dataset.pickerItem = JSON.stringify(item);
                const img = document.createElement('img');
                img.src = item.url;
                img.alt = item.alt || '';
                img.loading = 'lazy';
                const name = document.createElement('span');
                name.textContent = item.name;
                tile.append(img, name);
                grid.appendChild(tile);
            }
        } catch { msg.textContent = 'Không kết nối được máy chủ.'; }
    };

    const openPicker = (callback) => {
        ensureDialog();
        onPick = callback;
        dialog.querySelector('[data-picker-search]').value = '';
        dialog.showModal();
        load('');
    };

    document.addEventListener('click', (event) => {
        const pick = event.target.closest('[data-ta-image-pick]');
        const clear = event.target.closest('[data-ta-image-clear]');
        const box = (pick || clear)?.closest('[data-ta-web-image]');
        if (!box) return;
        const value = box.querySelector('[data-ta-image-value]');
        const preview = box.querySelector('[data-ta-image-preview]');
        const empty = box.querySelector('[data-ta-image-empty]');
        const set = (val, url) => {
            value.value = val;
            preview.src = url || '';
            preview.hidden = !url;
            if (empty) empty.hidden = !!url;
        };
        if (clear) { set('', ''); return; }
        openPicker((item) => set(box.dataset.taImageMode === 'id' ? item.id : item.value, item.url));
    });

    // ---- Article rich-text editor -------------------------------------------------------------------------------------
    const toAdmin = (html) => html.replaceAll('src="' + MEDIA_PUBLIC, 'src="' + MEDIA_ADMIN);
    const toPublic = (html) => html.replaceAll('src="' + MEDIA_ADMIN, 'src="' + MEDIA_PUBLIC).replace(/src="https?:\/\/[^"]*\/WebsiteMedia\/File\//g, 'src="' + MEDIA_PUBLIC);

    const initRte = (root) => {
        root.querySelectorAll('[data-ta-rte]:not([data-ready])').forEach((rte) => {
            rte.dataset.ready = '1';
            const area = rte.querySelector('[data-ta-rte-area]');
            const html = rte.querySelector('[data-ta-rte-html]');
            area.innerHTML = toAdmin(html.value || '<p></p>');
            document.execCommand?.('defaultParagraphSeparator', false, 'p');
        });
    };

    const sync = (rte) => {
        const area = rte.querySelector('[data-ta-rte-area]');
        const html = rte.querySelector('[data-ta-rte-html]');
        if (!html.hidden) { area.innerHTML = toAdmin(html.value); return; }
        html.value = toPublic(area.innerHTML).replace(/<p><br><\/p>$/i, '');
    };

    // Capture phase: runs before the shell's submit handler builds its FormData.
    document.addEventListener('submit', (event) => {
        event.target.querySelectorAll?.('[data-ta-rte]').forEach((rte) => {
            const html = rte.querySelector('[data-ta-rte-html]');
            if (html.hidden) sync(rte); else html.value = toPublic(html.value);
        });
    }, true);

    document.addEventListener('click', (event) => {
        const button = event.target.closest('[data-ta-rte] [data-cmd]');
        if (!button) return;
        event.preventDefault();
        const rte = button.closest('[data-ta-rte]');
        const area = rte.querySelector('[data-ta-rte-area]');
        const html = rte.querySelector('[data-ta-rte-html]');
        const cmd = button.dataset.cmd;
        if (cmd === 'html') {
            if (html.hidden) { sync(rte); html.hidden = false; area.hidden = true; button.classList.add('active'); }
            else { area.innerHTML = toAdmin(html.value); html.hidden = true; area.hidden = false; button.classList.remove('active'); }
            return;
        }
        if (!html.hidden) return;
        area.focus();
        if (cmd === 'link') {
            const url = window.prompt('Địa chỉ liên kết (https://... hoặc /lien-he):', 'https://');
            if (url && /^(https?:\/\/|\/|#|mailto:|tel:)/i.test(url)) document.execCommand('createLink', false, url);
        } else if (cmd === 'image') {
            const selection = window.getSelection();
            const range = selection.rangeCount ? selection.getRangeAt(0).cloneRange() : null;
            openPicker((item) => {
                area.focus();
                if (range) { selection.removeAllRanges(); selection.addRange(range); }
                const alt = (item.alt || '').replace(/"/g, '&quot;');
                document.execCommand('insertHTML', false, `<figure><img src="${MEDIA_ADMIN}${item.id}" alt="${alt}"><figcaption>${alt}</figcaption></figure><p></p>`);
            });
        } else if (cmd === 'callout') {
            document.execCommand('insertHTML', false, '<div class="ar-callout"><p><b>Lưu ý:</b> nội dung cần chú ý...</p></div><p></p>');
        } else if (cmd === 'table') {
            document.execCommand('insertHTML', false, '<table><thead><tr><th>Cột 1</th><th>Cột 2</th><th>Cột 3</th></tr></thead><tbody><tr><td></td><td></td><td></td></tr><tr><td></td><td></td><td></td></tr></tbody></table><p></p>');
        } else {
            document.execCommand(cmd, false, button.dataset.arg ? `<${button.dataset.arg}>` : null);
        }
    });

    // Pasted text from Word/websites: keep structure, drop styles (the server sanitizes again on save).
    document.addEventListener('paste', (event) => {
        const area = event.target.closest?.('[data-ta-rte-area]');
        if (!area) return;
        const htmlData = event.clipboardData?.getData('text/html');
        if (!htmlData) return;
        event.preventDefault();
        const doc = new DOMParser().parseFromString(htmlData, 'text/html');
        doc.querySelectorAll('script,style,meta,link,o\\:p').forEach((n) => n.remove());
        doc.body.querySelectorAll('*').forEach((n) => { [...n.attributes].forEach((a) => { if (!['href', 'src', 'alt', 'colspan', 'rowspan'].includes(a.name)) n.removeAttribute(a.name); }); });
        document.execCommand('insertHTML', false, doc.body.innerHTML);
    });

    // ---- Character counters ----------------------------------------------------------------------------------------
    const updateCount = (field) => {
        const limit = Number(field.dataset.taCount);
        let hint = field.parentElement.querySelector('.ta-web-count');
        if (!hint) { hint = document.createElement('small'); hint.className = 'ta-web-count'; field.after(hint); }
        const n = field.value.length;
        hint.textContent = `${n}/${limit} ký tự`;
        hint.classList.toggle('is-over', n > limit);
    };
    document.addEventListener('input', (event) => { if (event.target.matches?.('[data-ta-count]')) updateCount(event.target); });

    const init = () => {
        initRte(document);
        document.querySelectorAll('[data-ta-count]').forEach(updateCount);
        document.querySelectorAll('[data-ta-web-list]').forEach(renumber);
    };
    document.addEventListener('DOMContentLoaded', init);
    document.addEventListener('ta:load', init);
})();
