// Caches the game so it starts offline once it has been opened. Network first, so a new build is picked up when online.
const cacheName = {{{JSON.stringify(COMPANY_NAME + "-" + PRODUCT_NAME + "-" + PRODUCT_VERSION)}}};

self.addEventListener('install', function () { self.skipWaiting(); });

self.addEventListener('activate', function (e) {
  e.waitUntil((async function () {
    for (const key of await caches.keys()) { if (key !== cacheName) await caches.delete(key); }
    await self.clients.claim();
  })());
});

self.addEventListener('fetch', function (e) {
  if (e.request.method !== 'GET') return;
  e.respondWith((async function () {
    const cache = await caches.open(cacheName);
    try {
      const response = await fetch(e.request);
      if (response && response.ok && response.status === 200) cache.put(e.request, response.clone());
      return response;
    } catch (err) {
      const cached = await cache.match(e.request);
      if (cached) return cached;
      throw err;
    }
  })());
});
