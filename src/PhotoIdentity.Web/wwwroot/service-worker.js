const cacheName = 'photoidentity-review-shell-v1';

self.addEventListener('install', event => {
    event.waitUntil(self.skipWaiting());
});

self.addEventListener('activate', event => {
    event.waitUntil((async () => {
        const keys = await caches.keys();
        await Promise.all(keys.filter(key => key !== cacheName).map(key => caches.delete(key)));
        await self.clients.claim();
    })());
});

self.addEventListener('fetch', event => {
    if (event.request.method !== 'GET') {
        return;
    }

    const url = new URL(event.request.url);
    if (url.origin !== self.location.origin || url.pathname.startsWith('/api/')) {
        return;
    }

    event.respondWith((async () => {
        const cache = await caches.open(cacheName);
        try {
            const response = await fetch(event.request);
            if (response.ok) {
                await cache.put(event.request, response.clone());
            }
            return response;
        } catch {
            return (await cache.match(event.request)) ?? Response.error();
        }
    })());
});
