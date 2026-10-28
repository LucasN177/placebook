// In der Entwicklung keine Offline-Unterstützung – sonst sieht man nach Änderungen alte Dateien aus dem Cache.
// Beim Veröffentlichen wird diese Datei durch service-worker.published.js ersetzt.
self.addEventListener('fetch', () => { });
