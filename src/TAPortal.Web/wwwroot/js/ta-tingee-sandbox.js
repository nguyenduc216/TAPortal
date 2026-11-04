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
    if (status) {
        var text = status.querySelector('[data-ta-sandbox-qr-status-text]');
        var timer = setInterval(function () {
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
