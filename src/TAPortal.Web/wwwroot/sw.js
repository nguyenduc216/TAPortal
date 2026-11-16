/* T.A Portal service worker (operator 2026-11-16): makes the portal installable and shows push notifications.
   It caches nothing: admin pages are no-store and must always come from the server. */
'use strict';

self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', event => event.waitUntil(self.clients.claim()));

// Only same-origin pages may be opened from a notification.
function target(url) {
  try {
    const u = new URL(url || './', self.registration.scope);
    return u.origin === self.location.origin ? u.href : self.registration.scope;
  } catch (e) {
    return self.registration.scope;
  }
}

self.addEventListener('push', event => {
  let data = {};
  try { data = event.data ? event.data.json() : {}; } catch (e) { data = { body: event.data ? event.data.text() : '' }; }
  const scope = self.registration.scope;
  event.waitUntil(self.registration.showNotification(data.title || 'T.A Portal', {
    body: data.body || '',
    icon: scope + 'images/app-icon-192.png',
    badge: scope + 'images/notify-badge.png',
    tag: data.tag || undefined,
    renotify: !!data.tag,
    lang: 'vi',
    data: { url: target(data.url) }
  }));
});

self.addEventListener('notificationclick', event => {
  event.notification.close();
  const url = (event.notification.data && event.notification.data.url) || self.registration.scope;
  event.waitUntil((async () => {
    const windows = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
    for (const client of windows) {
      if (new URL(client.url).origin !== self.location.origin) continue;
      try {
        const opened = client.navigate ? await client.navigate(url) : null;
        return (opened || client).focus();
      } catch (e) {
        return client.focus();
      }
    }
    return self.clients.openWindow(url);
  })());
});
