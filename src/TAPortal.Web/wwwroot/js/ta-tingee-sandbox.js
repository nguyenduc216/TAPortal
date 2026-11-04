// Tingee Sandbox: polls the test QR's payment status (every 5 s) and refreshes the webhook list (every 10 s).
// Tingee log: copies a call report to the clipboard.
// Popups: "Xóa" fills the unlink popup with the chosen account; an OTP field gets the focus as soon as its popup opens.
(function () {
    'use strict';

    document.addEventListener('show.bs.modal', function (event) {
        var trigger = event.relatedTarget;
        var form = event.target.querySelector('#tgUnlinkForm');
        if (!form || !trigger || !trigger.hasAttribute('data-ta-unlink-bin')) return;
        form.querySelector('[data-ta-unlink-field="bin"]').value = trigger.getAttribute('data-ta-unlink-bin');
        form.querySelector('[data-ta-unlink-field="va"]').value = trigger.getAttribute('data-ta-unlink-va');
        form.querySelector('[data-ta-unlink-field="bank"]').textContent = trigger.getAttribute('data-ta-unlink-bank');
        form.querySelector('[data-ta-unlink-field="va-text"]').textContent = trigger.getAttribute('data-ta-unlink-va');
    });

    // Webhook addresses: copy the full address; the button turns green with a check and a "Đã copy" tooltip for 2 s.
    document.addEventListener('click', function (event) {
        var button = event.target.closest('[data-ta-copy-text]');
        if (!button) return;
        var text = button.getAttribute('data-ta-copy-text');
        var done = function () {
            button.classList.add('is-copied');
            var Tooltip = window.bootstrap && window.bootstrap.Tooltip;
            var tip = Tooltip ? Tooltip.getOrCreateInstance(button, { title: 'Đã copy', trigger: 'manual', placement: 'top' }) : null;
            if (tip) { tip.setContent({ '.tooltip-inner': 'Đã copy' }); tip.show(); }
            clearTimeout(button._taCopyTimer);
            button._taCopyTimer = setTimeout(function () { button.classList.remove('is-copied'); if (tip) tip.hide(); }, 2000);
        };
        var fallback = function () {
            var area = document.createElement('textarea');
            area.value = text; area.setAttribute('readonly', ''); area.style.position = 'fixed'; area.style.opacity = '0';
            document.body.appendChild(area); area.select(); document.execCommand('copy'); area.remove(); done();
        };
        if (navigator.clipboard && window.isSecureContext) navigator.clipboard.writeText(text).then(done, fallback);
        else fallback();
    });

    document.addEventListener('shown.bs.modal', function (event) {
        var otp = event.target.querySelector('[data-ta-otp-input]');
        if (otp) otp.focus();
    });

    document.addEventListener('click', function (event) {
        var button = event.target.closest('[data-ta-copy]');
        var source = button && document.querySelector(button.getAttribute('data-ta-copy'));
        if (!source) return;
        var value = source.value !== undefined ? source.value : source.textContent;
        var done = function () { var label = button.lastElementChild || button; var old = label.textContent; label.textContent = 'Đã chép'; setTimeout(function () { label.textContent = old; }, 1500); };
        var fallback = function () { window.getSelection().selectAllChildren(source); document.execCommand('copy'); done(); };
        if (navigator.clipboard) navigator.clipboard.writeText(value).then(done, fallback);
        else fallback();
    });

    var money = new Intl.NumberFormat('vi-VN');

    // Page timers (QR countdown, QR status, webhook list). The portal swaps <main> on form posts and links (soft navigation,
    // ta-shell.js), so they are started on the first load and again on every ta:load, and stopped on ta:before-swap.
    var timers = [];
    var every = function (fn, ms) { var id = setInterval(fn, ms); timers.push(id); return id; };
    var stopAll = function () { timers.forEach(clearInterval); timers = []; };

    var init = function () {
        stopAll();
        var status = document.querySelector('[data-ta-sandbox-qr-status]');
        var countdown = document.querySelector('[data-ta-qr-countdown]');
        var image = document.querySelector('[data-ta-qr-image]');
        var countdownTimer = null, timer = null, expired = false;

        if (countdown) {
            // Seconds left until the QR expires, counted from the server's figure at render time (independent of this PC's clock).
            // At 0 the QR is locked (blurred, "QR đã hết hạn") and the status polling stops.
            var expiresAt = Date.now() + (Number(countdown.getAttribute('data-seconds-left')) || 0) * 1000;
            var tick = function () {
                var left = Math.max(0, Math.ceil((expiresAt - Date.now()) / 1000));
                countdown.textContent = left > 0 ? 'còn ' + Math.floor(left / 60) + ':' + String(left % 60).padStart(2, '0') : 'đã hết hạn';
                countdown.classList.toggle('is-low', left > 0 && left <= 60);
                countdown.classList.toggle('is-over', left === 0);
                if (left === 0) {
                    expired = true;
                    clearInterval(countdownTimer);
                    if (image) image.classList.add('is-locked');
                    if (status && status.classList.contains('ta-alert--info')) {
                        clearInterval(timer);
                        status.classList.remove('ta-alert--info');
                        status.classList.add('ta-alert--warning');
                        status.querySelector('[data-ta-sandbox-qr-status-text]').textContent = 'QR đã hết hạn, chưa nhận đủ tiền. Tạo QR mới để thử tiếp.';
                    }
                }
            };
            tick();
            if (!expired) countdownTimer = every(tick, 1000);
        }

        if (status && !expired) {
            var text = status.querySelector('[data-ta-sandbox-qr-status-text]');
            timer = every(function () {
                fetch(status.getAttribute('data-url'), { headers: { 'Accept': 'application/json' }, credentials: 'same-origin' })
                    .then(function (r) { return r.json(); })
                    .then(function (s) {
                        if (expired) return;
                        if (!s.ok) { text.textContent = 'Không kiểm tra được trạng thái: ' + s.error; return; }
                        var paid = Number(s.paid) || 0, amount = Number(s.amount) || 0;
                        text.textContent = 'Trạng thái: ' + (s.status || '—') + ' — đã nhận ' + money.format(paid) + 'đ / ' + money.format(amount) + 'đ ('
                            + s.payments + ' lần), kiểm tra lúc ' + s.checkedAt;
                        if (amount > 0 && paid >= amount) {
                            status.classList.remove('ta-alert--info');
                            status.classList.add('ta-alert--success');
                            text.textContent = 'ĐÃ NHẬN ĐỦ TIỀN: ' + money.format(paid) + 'đ (' + s.payments + ' lần), lúc ' + s.checkedAt;
                            clearInterval(timer);
                            clearInterval(countdownTimer);
                            if (countdown) { countdown.textContent = 'đã thanh toán'; countdown.classList.remove('is-low', 'is-over'); countdown.classList.add('is-paid'); }
                            if (image) { image.classList.add('is-locked', 'is-paid'); image.querySelector('[data-ta-qr-lock-text]').textContent = 'Đã thanh toán'; }
                        }
                    })
                    .catch(function () { text.textContent = 'Mất kết nối, đang thử lại…'; });
            }, 5000);
        }

        var hooks = document.querySelector('[data-ta-sandbox-webhooks]');
        if (hooks) {
            every(function () {
                fetch(hooks.getAttribute('data-url'), { credentials: 'same-origin' })
                    .then(function (r) { return r.ok ? r.text() : null; })
                    .then(function (html) { if (html !== null) { hooks.innerHTML = html; } })
                    .catch(function () { /* keep the last list */ });
            }, 10000);
        }
    };

    // Bank Link SDK trial (section 5): the server opens a create-bank-link-session and returns the SDK URL and Tingee's
    // script; the script is loaded once, then the session opens as a popup or inside the page. Every callback is listed.
    // Capture phase: runs before the shell's soft-navigation submit handler, which would otherwise post the form.
    var sdkScripts = {};
    var loadSdk = function (src) {
        if (!sdkScripts[src]) {
            sdkScripts[src] = new Promise(function (resolve, reject) {
                var tag = document.createElement('script');
                tag.src = src; tag.async = true;
                tag.onload = function () { window.TingeeBankLink ? resolve(window.TingeeBankLink) : reject(new Error('không thấy TingeeBankLink')); };
                tag.onerror = function () { delete sdkScripts[src]; reject(new Error('không tải được ' + src)); };
                document.head.appendChild(tag);
            });
        }
        return sdkScripts[src];
    };

    document.addEventListener('submit', function (event) {
        var form = event.target.closest && event.target.closest('[data-ta-banklink-form]');
        if (!form) return;
        event.preventDefault();
        var card = form.closest('[data-ta-sandbox-sdk]');
        var status = card.querySelector('[data-ta-banklink-status]');
        var statusText = card.querySelector('[data-ta-banklink-status-text]');
        var log = card.querySelector('[data-ta-banklink-log]');
        var inline = card.querySelector('[data-ta-banklink-inline]');
        var button = form.querySelector('[data-ta-banklink-open]');
        var mode = form.querySelector('[data-ta-banklink-mode]').value;
        var say = function (tone, text) {
            status.hidden = false;
            status.className = 'ta-alert ta-alert--' + tone + ' mt-3 mb-0';
            statusText.textContent = text;
        };
        var note = function (name, data) {
            log.hidden = false;
            var item = document.createElement('li');
            var time = new Date().toLocaleTimeString('vi-VN');
            item.textContent = time + ' ' + name + (data === undefined ? '' : ': ' + JSON.stringify(data));
            log.appendChild(item);
        };
        button.disabled = true;
        say('info', 'Đang tạo phiên SDK…');
        fetch(form.action, { method: 'POST', body: new FormData(form), credentials: 'same-origin', headers: { 'Accept': 'application/json' } })
            .then(function (r) { return r.json(); })
            .then(function (s) {
                if (!s.ok) throw new Error(s.error);
                return loadSdk(s.script).then(function (sdk) {
                    sdk.Setup({
                        size: 'medium',
                        theme: { primaryColor: '#206bc4' },
                        onSuccess: function (d) { note('onSuccess', d); say('success', 'Liên kết thành công' + (d && d.vaAccountNumber ? ': tài khoản nhận ' + d.vaAccountNumber : '') + '. Tải lại trang để thấy ở mục 2; webhook bank-link sẽ hiện ở mục 4.'); },
                        onFailed: function (d) { note('onFailed', d); say('danger', 'Liên kết không thành công: ' + ((d && (d.errorMessage || d.errorCode)) || 'không rõ lý do')); },
                        onCancelled: function (d) { note('onCancelled', d); say('warning', 'Đã đóng SDK, chưa liên kết.'); },
                        onError: function (d) { note('onError', d); say('danger', 'SDK báo lỗi hệ thống.'); },
                        onClose: function () { note('onClose'); }
                    });
                    note('Mở phiên', { mode: mode });
                    if (mode === 'inline') {
                        inline.hidden = false;
                        inline.innerHTML = '';
                        sdk.Url.Open(s.url, { containerId: inline.id });
                    } else {
                        sdk.Url.Open(s.url);
                    }
                    say('info', 'Đã mở SDK. Thao tác trong màn hình của Tingee.');
                });
            })
            .catch(function (e) { say('danger', e.message || 'Không mở được SDK.'); })
            .then(function () { button.disabled = false; });
    }, true);

    init();
    document.addEventListener('ta:load', init);
    document.addEventListener('ta:before-swap', stopAll);
})();
