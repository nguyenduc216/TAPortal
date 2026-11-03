// Tingee Sandbox: polls the test QR's payment status (every 5 s) and refreshes the webhook list (every 10 s).
(function () {
    'use strict';

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
