(() => {
    'use strict';
    if (window.TAPortalRecovery) return;
    const copy = { disconnected: ['Hệ thống đang gián đoạn', 'Không thể kết nối với máy chủ. Hệ thống đang tự động kiểm tra và thử kết nối lại.'], reconnecting: ['Đang kết nối lại', 'Hệ thống đang kết nối. Vui lòng chờ trong ít phút...'], maintenance: ['Hệ thống đang được cập nhật', 'Hệ thống đang được bảo trì và sẽ hoạt động trở lại trong ít phút. Vui lòng chờ...'], recovered: ['Kết nối đã khôi phục', 'Hệ thống đã kết nối lại với máy chủ và sẵn sàng hoạt động.'], restart: ['Máy chủ đã hoạt động trở lại', 'Phiên làm việc cần được tải lại để tiếp tục sử dụng.'] };
    // CHANGE_ID: TAPORTAL-20261012-RECOVERY-014 — independent timers and cycle tokens prevent heartbeat cancellation and stale dismissals.
    let host, title, message, detail, reload, retry, observer, previousFocus;
    let state = 'hidden', probeTimer, dismissTimer, cycle = 0, attempts = 0;
    let disposed = false, probing = false, reloadUsed = false, requestController;
    let lastProbeAt = 0, lastHttpStatus = null, lastTransitionReason = 'init', nextProbeDelay = 0;
    let circuitState = 'connected', circuitClass = '', reconnecting = false;
    const listeners = new AbortController();
    const healthyDelay = 7000, failureThreshold = 2, requestTimeout = 4500;
    const nextDelay = () => Math.min(30000, 1000 * Math.pow(1.6, Math.min(attempts, 8)) + Math.random() * 700);
    const circuitModal = () => document.getElementById('components-reconnect-modal');

    // CHANGE_ID: TAPORTAL-20261012-RECOVERY-014 — leave the framework-owned modal/classes intact; eagerly load the logo before an outage.
    function ensureHost() {
        if (host?.isConnected) return;
        host = document.getElementById('ta-recovery-overlay');
        if (!host) {
            host = document.createElement('div');
            host.id = 'ta-recovery-overlay';
            host.className = 'ta-recovery-overlay';
            host.hidden = true;
            host.innerHTML = '<div class="ta-recovery-card" role="dialog" aria-modal="true" aria-labelledby="ta-recovery-title" aria-describedby="ta-recovery-message" tabindex="-1"><div class="ta-master-loading ta-recovery-visual" aria-hidden="true"><span class="ta-master-loading-ring"></span><img src="/images/ta-logo.png" alt="" width="52" height="52" loading="eager" /></div><h2 id="ta-recovery-title"></h2><p id="ta-recovery-message" aria-live="polite"></p><p class="ta-recovery-detail"></p><div class="ta-recovery-actions"><button class="btn btn-primary" data-ta-retry type="button">Thử kết nối lại</button><button class="btn btn-primary" data-ta-reload type="button">Tải lại và tiếp tục</button></div></div>';
            document.body.appendChild(host);
        }
        title = host.querySelector('#ta-recovery-title');
        message = host.querySelector('#ta-recovery-message');
        detail = host.querySelector('.ta-recovery-detail');
        reload = host.querySelector('[data-ta-reload]');
        retry = host.querySelector('[data-ta-retry]');
        retry.onclick = () => { attempts = 0; poll(); };
        reload.onclick = () => safeReload(true);
    }

    // CHANGE_ID: TAPORTAL-20261012-RECOVERY-014 — release the interaction surface and restore focus only for the same successful cycle.
    function setState(next, reason = 'state-change') {
        if (disposed) return;
        ensureHost();
        if (next === state) return;
        clearTimeout(dismissTimer);
        const token = ++cycle;
        if (state === 'hidden' && next !== 'hidden') previousFocus = document.activeElement;
        lastTransitionReason = reason;
        state = next;
        host.hidden = next === 'hidden';
        host.dataset.state = next;
        if (next === 'hidden') {
            if (host.contains(document.activeElement) && previousFocus?.isConnected) previousFocus.focus({ preventScroll: true });
            previousFocus = null;
        } else {
            title.textContent = copy[next][0];
            message.textContent = copy[next][1];
            detail.textContent = next === 'reconnecting' ? `Lần thử ${attempts + 1}` : '';
            reload.hidden = next !== 'restart';
            retry.hidden = next === 'restart' || next === 'recovered';
            host.querySelector('.ta-recovery-card').focus({ preventScroll: true });
        }
        if (next === 'recovered') dismissTimer = setTimeout(() => {
            if (!disposed && token === cycle && state === 'recovered') setState('hidden', 'recovered-dismissed');
        }, 1500);
    }

    // CHANGE_ID: TAPORTAL-20261012-RECOVERY-014 — abort outstanding readiness work on disposal.
    async function readiness() {
        requestController = new AbortController();
        const controller = requestController;
        const timeout = setTimeout(() => controller.abort(), requestTimeout);
        lastProbeAt = Date.now();
        lastHttpStatus = null;
        try {
            const response = await fetch(`/health/client-readiness?_ta_probe=${lastProbeAt}`, { cache: 'no-store', credentials: 'same-origin', signal: controller.signal });
            lastHttpStatus = response.status;
            if (!response.ok || !(response.headers.get('content-type') || '').toLowerCase().includes('json')) return null;
            const value = await response.json();
            return value && typeof value === 'object' ? value : null;
        } catch { return null; } // Preserve a received HTTP status even when JSON parsing fails.
        finally { clearTimeout(timeout); if (requestController === controller) requestController = null; }
    }
    // CHANGE_ID: TAPORTAL-20261012-RECOVERY-014 — heartbeat scheduling must never cancel the success timer.
    function schedule(delay) {
        clearTimeout(probeTimer);
        nextProbeDelay = delay;
        if (!disposed) probeTimer = setTimeout(poll, delay);
    }

    // CHANGE_ID: TAPORTAL-20261012-RECOVERY-014 — only a framework reconnect result/class can confirm a lost Blazor circuit.
    async function recoverCircuit() {
        if (reconnecting || circuitState === 'restart' || typeof window.Blazor?.reconnect !== 'function') return;
        reconnecting = true;
        const token = cycle;
        try {
            const connected = await window.Blazor.reconnect();
            if (disposed || token !== cycle) return;
            if (connected === true) { circuitState = 'connected'; setState('recovered', 'framework-reconnect-confirmed'); }
            else if (connected === false) { circuitState = 'restart'; setState('restart', 'framework-reconnect-rejected'); }
        } catch { /* The framework may still be reconnecting; keep the blocking recovery state. */ }
        finally { reconnecting = false; }
    }

    // CHANGE_ID: TAPORTAL-20261012-RECOVERY-014 — readiness confirms MVC recovery, never overrides a rejected/disconnected circuit.
    async function poll() {
        if (disposed || probing || document.hidden) return;
        clearTimeout(probeTimer);
        probing = true;
        const token = cycle;
        const result = await readiness();
        probing = false;
        if (disposed) return;
        if (token !== cycle) { schedule(nextDelay()); return; }
        if (result?.ready === true && result.maintenance !== true) {
            attempts = 0;
            if (state !== 'hidden' && state !== 'recovered' && state !== 'restart') {
                if (!circuitModal() || circuitState === 'connected') setState('recovered', 'readiness-restored');
                else await recoverCircuit();
            }
            schedule(healthyDelay + Math.random() * 1000);
            return;
        }
        attempts++;
        if (circuitModal() && circuitState === 'connected') circuitState = 'awaiting';
        if (state !== 'restart') {
            if (result?.maintenance === true) setState('maintenance', 'readiness-maintenance');
            else if (attempts >= failureThreshold) setState('disconnected', `readiness-failure-${attempts}`);
            else setState('reconnecting', `readiness-failure-${attempts}`);
        }
        schedule(nextDelay());
    }
    // CHANGE_ID: TAPORTAL-20261012-RECOVERY-014 — include standalone dirty inputs in reload protection.
    function dirty() { return !!document.querySelector('form[data-ta-dirty="true"], [data-ta-unsaved="true"], [data-ta-dirty-input][data-ta-dirty="true"]'); }
    // CHANGE_ID: TAPORTAL-20261012-RECOVERY-014 — never reload automatically or discard unsaved changes without explicit confirmation.
    function safeReload(confirmed) {
        if (!confirmed || (dirty() && !window.confirm('Bạn có thay đổi chưa lưu. Tải lại và bỏ các thay đổi này?'))) return;
        if (reloadUsed) return;
        reloadUsed = true;
        location.reload();
    }
    // CHANGE_ID: TAPORTAL-20261012-RECOVERY-014 — observe only framework lifecycle changes, not unrelated class mutations.
    function circuitChanged() {
        const modal = circuitModal();
        if (!modal || modal.className === circuitClass) return;
        circuitClass = modal.className;
        if (modal.classList.contains('components-reconnect-rejected') || modal.classList.contains('components-reconnect-failed')) {
            circuitState = 'restart'; setState('restart', 'framework-restart-required');
        } else if (modal.classList.contains('components-reconnect-show')) {
            circuitState = 'awaiting'; attempts = Math.max(attempts, failureThreshold);
            setState(navigator.onLine ? 'reconnecting' : 'disconnected', 'framework-disconnected');
            poll();
        } else if (modal.classList.contains('components-reconnect-hide') && circuitState !== 'restart') {
            circuitState = 'connected';
            if (state !== 'hidden') setState('recovered', 'framework-circuit-restored');
        }
    }
    function markDirty(target) { const form = target.closest('form'); if (form && !target.matches('[type="search"], [data-ta-ignore-dirty]')) form.dataset.taDirty = 'true'; else if (target.matches('[data-ta-dirty-input]')) target.dataset.taDirty = 'true'; }
    // CHANGE_ID: TAPORTAL-20261011-BLAZOR-UI-RECOVERY-012 — only an explicit reset or confirmed save clears dirty state.
    function clearDirty(target) { const form = target?.closest?.('form') || (target instanceof HTMLFormElement ? target : null); if (form) delete form.dataset.taDirty; }

    // CHANGE_ID: TAPORTAL-20261012-RECOVERY-014 — remove listeners, requests and both timers; bfcache pages pause and resume the existing coordinator.
    function dispose() {
        disposed = true; ++cycle;
        observer?.disconnect(); listeners.abort(); requestController?.abort();
        clearTimeout(probeTimer); clearTimeout(dismissTimer);
        if (retry) retry.onclick = null;
        if (reload) reload.onclick = null;
        if (host) host.hidden = true;
    }
    function init() {
        if (disposed) return;
        ensureHost();
        const modal = circuitModal();
        if (modal) { observer = new MutationObserver(circuitChanged); observer.observe(modal, { attributes: true, attributeFilter: ['class'] }); circuitChanged(); }
        const options = { signal: listeners.signal };
        document.addEventListener('input', event => markDirty(event.target), options);
        document.addEventListener('change', event => markDirty(event.target), options);
        document.addEventListener('reset', event => clearDirty(event.target), { ...options, capture: true });
        addEventListener('offline', () => {
            attempts = failureThreshold;
            if (modal && circuitState !== 'restart') circuitState = 'awaiting';
            if (state !== 'restart') setState('disconnected', 'browser-offline');
            schedule(nextDelay());
        }, options);
        addEventListener('online', () => { if (state !== 'restart') setState('reconnecting', 'browser-online'); poll(); }, options);
        document.addEventListener('visibilitychange', () => { if (!document.hidden) poll(); }, options);
        addEventListener('pagehide', event => { if (!event.persisted) dispose(); else { clearTimeout(probeTimer); clearTimeout(dismissTimer); } }, options);
        addEventListener('pageshow', event => { if (event.persisted) { if (state === 'recovered') setState('hidden', 'bfcache-restored'); poll(); } }, options);
        poll();
    }
    window.TAPortalRecovery = { getState: () => state, show: setState, clearDirty, markDirty,
        getDiagnostics: () => ({ schedulerRunning: !disposed, lastProbeAt, lastHttpStatus, consecutiveFailures: attempts, currentState: state, nextProbeDelay, lastTransitionReason, circuitState }), dispose };
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init, { once: true, signal: listeners.signal }); else init();
})();
