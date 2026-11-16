/* Cài ứng dụng + thông báo đẩy (operator 2026-11-16).
   - Registers wwwroot/sw.js so the portal can be installed (icon on the phone / computer) and receive notifications.
   - [data-ta-app-install] buttons appear when the browser offers installation; iPhone/iPad get the "Add to Home Screen" hint.
   - [data-ta-push] (Hồ sơ cá nhân) turns notifications on/off for this device and sends a test notification.
   The shell swaps <main>, so everything is delegated and re-initialised on ta:load. */
(function () {
  'use strict';
  const meta = document.querySelector('meta[name="ta-app"]');
  if (!meta) return;
  const urls = meta.dataset;
  const ua = navigator.userAgent || '';
  const isIos = /iPad|iPhone|iPod/.test(ua) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
  const standalone = () => window.matchMedia('(display-mode: standalone)').matches || navigator.standalone === true;
  const swSupported = 'serviceWorker' in navigator && window.isSecureContext;
  const pushSupported = swSupported && 'PushManager' in window && 'Notification' in window;
  const identity = () => document.body.dataset.taIdentity || '';
  const impersonating = () => identity().indexOf('/') >= 0;
  const SYNC = 'ta-push-sync';
  let installPrompt = null;
  let registration = null;

  function store(key, value) {
    try { if (value === undefined) return localStorage.getItem(key); if (value === null) localStorage.removeItem(key); else localStorage.setItem(key, value); } catch (e) { /* private mode */ }
    return null;
  }

  function token() {
    const input = document.querySelector('input[name="__RequestVerificationToken"]');
    return input ? input.value : '';
  }

  function b64(buffer) {
    const bytes = new Uint8Array(buffer);
    let s = '';
    for (let i = 0; i < bytes.length; i++) s += String.fromCharCode(bytes[i]);
    return btoa(s).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  }

  function unb64(text) {
    const s = (text + '==='.slice((text.length + 3) % 4)).replace(/-/g, '+').replace(/_/g, '/');
    const raw = atob(s);
    const out = new Uint8Array(raw.length);
    for (let i = 0; i < raw.length; i++) out[i] = raw.charCodeAt(i);
    return out;
  }

  async function post(url, fields) {
    const body = new FormData();
    body.append('__RequestVerificationToken', token());
    Object.keys(fields || {}).forEach(k => body.append(k, fields[k]));
    const res = await fetch(url, { method: 'POST', body, credentials: 'same-origin', headers: { 'X-Requested-With': 'fetch' } });
    let data = {};
    try { data = await res.json(); } catch (e) { /* not JSON */ }
    if (!res.ok) throw new Error(data.error || 'Không thực hiện được, vui lòng thử lại.');
    return data;
  }

  async function ready() {
    if (!swSupported) return null;
    if (!registration) registration = await navigator.serviceWorker.register(urls.sw, { scope: urls.scope });
    return navigator.serviceWorker.ready;
  }

  async function currentSubscription() {
    if (!pushSupported) return null;
    const reg = await ready();
    return reg ? reg.pushManager.getSubscription() : null;
  }

  async function save(sub) {
    const json = sub.toJSON();
    await post(urls.subscribe, { endpoint: sub.endpoint, p256dh: json.keys.p256dh, auth: json.keys.auth });
    store(SYNC, JSON.stringify({ who: identity(), endpoint: sub.endpoint, at: Date.now() }));
  }

  async function enable() {
    if (impersonating()) throw new Error('Không bật thông báo khi đang truy cập hộ.');
    const permission = await Notification.requestPermission();
    if (permission !== 'granted') throw new Error(permission === 'denied' ? 'Bạn đã chặn thông báo cho trang này.' : 'Bạn chưa cho phép thông báo.');
    const reg = await ready();
    const res = await fetch(urls.key, { credentials: 'same-origin' });
    if (!res.ok) throw new Error('Máy chủ chưa sẵn sàng gửi thông báo.');
    const key = (await res.json()).publicKey;
    let sub = await reg.pushManager.getSubscription();
    if (sub && sub.options && sub.options.applicationServerKey && b64(sub.options.applicationServerKey) !== key) { await sub.unsubscribe(); sub = null; }
    if (!sub) sub = await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: unb64(key) });
    await save(sub);
  }

  async function disable() {
    const sub = await currentSubscription();
    if (sub) {
      try { await post(urls.unsubscribe, { endpoint: sub.endpoint }); } catch (e) { /* removed on the device anyway */ }
      await sub.unsubscribe();
    }
    store(SYNC, null);
  }

  // Keeps the server's copy fresh (weekly) for the person who turned it on; a device left subscribed by someone else
  // who signed in here before is unsubscribed, so nobody receives another person's notifications.
  async function sync() {
    if (!pushSupported || !document.body.dataset.taIdentity || Notification.permission !== 'granted' || impersonating()) return;
    const sub = await currentSubscription();
    if (!sub) return;
    let last = null;
    try { last = JSON.parse(store(SYNC) || 'null'); } catch (e) { last = null; }
    if (!last || last.who !== identity()) { await sub.unsubscribe(); store(SYNC, null); return; }
    if (last.endpoint === sub.endpoint && Date.now() - last.at < 7 * 864e5) return;
    if (token()) await save(sub);
  }

  // ---- UI ---------------------------------------------------------------------------------------------------------
  function show(el, on) { if (el) el.hidden = !on; }

  function paintInstall() {
    const installed = standalone();
    document.querySelectorAll('[data-ta-app-install]').forEach(b => show(b, !!installPrompt && !installed));
    document.querySelectorAll('[data-ta-app]').forEach(root => {
      if (root.hasAttribute('data-ta-app-hide-installed')) { root.hidden = installed; if (installed) return; }
      const status = root.querySelector('[data-ta-app-status]');
      if (status) status.textContent = installed ? 'Bạn đang dùng T.A Portal dạng ứng dụng trên thiết bị này.'
        : installPrompt ? 'Có thể cài T.A Portal lên thiết bị này.'
        : isIos ? 'Trên iPhone/iPad: cài bằng Safari theo hướng dẫn bên dưới.'
        : 'Cài theo hướng dẫn bên dưới.';
      show(root.querySelector('[data-ta-app-ios]'), isIos && !installed);
      show(root.querySelector('[data-ta-app-manual]'), !isIos && !installed && !installPrompt);
    });
  }

  async function paintPush() {
    const roots = document.querySelectorAll('[data-ta-push]');
    if (!roots.length) return;
    let state = 'off', sub = null;
    if (!pushSupported) state = isIos && !standalone() ? 'ios' : 'unsupported';
    else if (impersonating()) state = 'impersonating';
    else if (Notification.permission === 'denied') state = 'denied';
    else {
      try { sub = await currentSubscription(); } catch (e) { sub = null; }
      if (sub && Notification.permission === 'granted') state = 'on';
    }
    const text = {
      on: 'Đang bật trên thiết bị này.',
      off: 'Chưa bật trên thiết bị này.',
      denied: 'Trình duyệt đang chặn thông báo của trang này. Mở cài đặt trang (biểu tượng cạnh thanh địa chỉ) > Thông báo > Cho phép, rồi bấm Bật thông báo.',
      ios: 'Trên iPhone/iPad, cần cài T.A Portal vào màn hình chính (iOS 16.4 trở lên) rồi mở từ biểu tượng đó để bật thông báo.',
      unsupported: 'Trình duyệt này không hỗ trợ thông báo đẩy. Hãy dùng Chrome, Edge, Firefox hoặc Safari bản mới.',
      impersonating: 'Không bật thông báo khi đang truy cập hộ.'
    }[state];
    roots.forEach(root => {
      root.dataset.state = state;
      const status = root.querySelector('[data-ta-push-status]');
      if (status) status.textContent = text;
      show(root.querySelector('[data-ta-push-on]'), state === 'off' || state === 'denied');
      show(root.querySelector('[data-ta-push-off]'), state === 'on');
      show(root.querySelector('[data-ta-push-test]'), state === 'on');
    });
  }

  function message(root, text, ok) {
    const box = root && root.querySelector('[data-ta-push-msg]');
    if (!box) return;
    box.textContent = text || '';
    box.className = 'ta-push__msg ' + (ok ? 'text-success' : 'text-danger');
    box.hidden = !text;
  }

  async function busy(button, work) {
    const root = button.closest('[data-ta-push]');
    button.disabled = true;
    message(root, '');
    try { const text = await work(); message(root, text, true); }
    catch (e) { message(root, e && e.message ? e.message : 'Không thực hiện được, vui lòng thử lại.', false); }
    finally { button.disabled = false; await paintPush(); }
  }

  document.addEventListener('click', e => {
    const install = e.target.closest('[data-ta-app-install]');
    if (install && installPrompt) {
      e.preventDefault();
      const p = installPrompt;
      installPrompt = null;
      p.prompt();
      p.userChoice.finally(paintInstall);
      return;
    }
    const on = e.target.closest('[data-ta-push-on]');
    if (on) { e.preventDefault(); busy(on, async () => { await enable(); return 'Đã bật thông báo cho thiết bị này.'; }); return; }
    const off = e.target.closest('[data-ta-push-off]');
    if (off) { e.preventDefault(); busy(off, async () => { await disable(); return 'Đã tắt thông báo cho thiết bị này.'; }); return; }
    const test = e.target.closest('[data-ta-push-test]');
    if (test) {
      e.preventDefault();
      busy(test, async () => {
        const r = await post(urls.test, {});
        if (!r.sent) throw new Error('Chưa gửi được. Hãy tắt rồi bật lại thông báo trên thiết bị này.');
        return 'Đã gửi tới ' + r.sent + ' thiết bị. Thông báo sẽ hiện trong vài giây.';
      });
    }
  });

  // Signing out: this device stops receiving the person's notifications.
  document.addEventListener('submit', e => {
    const form = e.target;
    if (!form.matches || !form.matches('form[data-ta-logout]')) return;
    // Synchronous on purpose: the page leaves right after. The browser-side subscription is dropped by sync() on the
    // next sign-in (no record for that person), and the push service then reports it gone.
    let last = null;
    try { last = JSON.parse(store(SYNC) || 'null'); } catch (err) { last = null; }
    store(SYNC, null);
    if (!last || !last.endpoint || !navigator.sendBeacon) return;
    const fd = new FormData();
    fd.append('__RequestVerificationToken', token());
    fd.append('endpoint', last.endpoint);
    navigator.sendBeacon(urls.unsubscribe, fd);
  }, true);

  window.addEventListener('beforeinstallprompt', e => { e.preventDefault(); installPrompt = e; paintInstall(); });
  window.addEventListener('appinstalled', () => { installPrompt = null; paintInstall(); });

  function init() { paintInstall(); paintPush(); }
  document.addEventListener('ta:load', init);
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
  if (swSupported) window.addEventListener('load', () => { ready().then(sync).catch(() => {}); });
})();
