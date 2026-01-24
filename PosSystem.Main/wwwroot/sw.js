// Minimal Service Worker for installability (Android) and basic offline caching.
// Note: Keep this conservative; avoid caching API responses.

const CACHE_NAME = 'lp-pos-static-v2';
const PRECACHE_URLS = [
    '/',
    '/index.html',
    '/mobile.html',
    '/css/mobile-style.css',
    '/vendor/fontawesome/css/all.min.css',
    '/vendor/fontawesome/webfonts/fa-solid-900.woff2',
    '/vendor/fontawesome/webfonts/fa-solid-900.woff',
    '/vendor/fontawesome/webfonts/fa-regular-400.woff2',
    '/vendor/fontawesome/webfonts/fa-regular-400.woff',
    '/vendor/fontawesome/webfonts/fa-brands-400.woff2',
    '/vendor/fontawesome/webfonts/fa-brands-400.woff',
    '/js/mobile-app.js',
    '/js/signalr.min.js',
    '/manifest.webmanifest',
    '/assets/app.ico'
];

self.addEventListener('install', (event) => {
    event.waitUntil(
        caches.open(CACHE_NAME)
            .then((cache) => cache.addAll(PRECACHE_URLS))
            .catch(() => { })
    );
    self.skipWaiting();
});

self.addEventListener('activate', (event) => {
    event.waitUntil(
        caches.keys().then((keys) =>
            Promise.all(keys
                .filter((k) => k !== CACHE_NAME)
                .map((k) => caches.delete(k))
            )
        )
    );
    self.clients.claim();
});

self.addEventListener('fetch', (event) => {
    const req = event.request;
    const url = new URL(req.url);

    // Only handle same-origin GET
    if (req.method !== 'GET' || url.origin !== self.location.origin) return;

    // Never cache API/SignalR traffic
    if (url.pathname.startsWith('/api/') || url.pathname.startsWith('/posHub')) return;

    // Cache-first for static assets
    event.respondWith(
        caches.match(req).then((cached) => {
            if (cached) return cached;
            return fetch(req)
                .then((res) => {
                    // Cache only successful, basic responses
                    if (res && res.status === 200 && res.type === 'basic') {
                        const copy = res.clone();
                        caches.open(CACHE_NAME).then((cache) => cache.put(req, copy)).catch(() => { });
                    }
                    return res;
                })
                .catch(() => cached);
        })
    );
});
