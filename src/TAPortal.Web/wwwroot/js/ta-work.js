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
})();
