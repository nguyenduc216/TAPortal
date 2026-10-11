(() => {
    'use strict';
    const modal = document.getElementById('components-reconnect-modal');
    if (!modal) return;
    const title = modal.querySelector('[data-ta-reconnect-title]');
    const message = modal.querySelector('[data-ta-reconnect-message]');
    const retry = modal.querySelector('[data-ta-reconnect-retry]');
    const reload = modal.querySelector('[data-ta-reconnect-reload]');
    let attempts = 0;

    const show = (state) => {
        modal.classList.add('components-reconnect-show');
        modal.setAttribute('aria-hidden', 'false');
        if (state === 'failed') { title.textContent = 'Không thể kết nối tới máy chủ'; message.textContent = 'Chưa thể kết nối. Vui lòng thử lại hoặc tải lại trang.'; }
        else if (state === 'reconnecting') { title.textContent = 'Đang kết nối lại hệ thống'; message.textContent = `Chưa thể kết nối. Đang thử lại sau ${Math.max(1, Math.min(attempts, 10))} giây...`; }
        else { title.textContent = 'Đang kết nối lại hệ thống'; message.textContent = 'Máy chủ có thể đang được cập nhật. Hệ thống sẽ tự kết nối lại khi sẵn sàng.'; }
    };
    const hide = () => { modal.setAttribute('aria-hidden', 'true'); attempts = 0; };
    retry.addEventListener('click', () => window.location.reload());
    reload.addEventListener('click', () => window.location.reload());
    const observer = new MutationObserver(() => {
        if (modal.classList.contains('components-reconnect-failed') || modal.classList.contains('components-reconnect-rejected')) show('failed');
        else if (modal.classList.contains('components-reconnect-show')) { attempts++; show('reconnecting'); }
        else if (modal.classList.contains('components-reconnect-hide')) hide();
    });
    observer.observe(modal, { attributes: true, attributeFilter: ['class'] });
})();