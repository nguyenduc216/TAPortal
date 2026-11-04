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

    var status = document.querySelector('[data-ta-sandbox-qr-status]');
    var countdown = document.querySelector('[data-ta-qr-countdown]');
    var countdownTimer = null, timer = null, expired = false;
    if (countdown) {
        // Seconds left until the QR expires, counted from the server's figure at page load (independent of this PC's clock).
        // At 0 the QR is locked (blurred, "QR đã hết hạn") and the status polling stops.
        var image = document.querySelector('[data-ta-qr-image]');
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
        countdownTimer = setInterval(tick, 1000);
    }
    if (status && !expired) {
        var text = status.querySelector('[data-ta-sandbox-qr-status-text]');
        timer = setInterval(function () {
            fetch(status.getAttribute('data-url'), { headers: { 'Accept': 'application/json' }, credentials: 'same-origin' })
                .then(function (r) { return r.json(); })
                .then(function (s) {
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
                        var paidImage = document.querySelector('[data-ta-qr-image]');
                        if (paidImage) { paidImage.classList.add('is-locked', 'is-paid'); paidImage.querySelector('[data-ta-qr-lock-text]').textContent = 'Đã thanh toán'; }
                    }
                })
                .catch(function () { text.textContent = 'Mất kết nối, đang thử lại…'; });
        }, 5000);
    }

    var hooks = document.querySelector('[data-ta-sandbox-webhooks]');
    if (hooks) {
        setInterval(function () {
            fetch(hooks.getAttribute('data-url'), { credentials: 'same-origin' })
                .then(function (r) { return r.ok ? r.text() : null; })
                .then(function (html) { if (html !== null) { hooks.innerHTML = html; } })
                .catch(function () { /* keep the last list */ });
        }, 10000);
    }
})();
