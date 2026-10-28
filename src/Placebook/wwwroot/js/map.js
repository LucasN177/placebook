// Leaflet-Interop für Placebook. Alle Marker werden hier in JS erzeugt – bei ~10.000 Orten wäre ein
// Interop-Aufruf pro Marker zu langsam. Blazor übergibt die Orte einmal gesammelt.

const EUROPE = { center: [50.5, 10.0], zoom: 4 };
const VIEW_KEY = 'placebook.map.view';

const maps = new Map();
let nextId = 1;

export function createMap(element, dotNetRef) {
    const view = loadView() ?? EUROPE;
    const map = L.map(element, {
        center: view.center,
        zoom: view.zoom,
        minZoom: 3,
        maxZoom: 18,
        worldCopyJump: true,
        preferCanvas: true,
    });

    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19,
        attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>-Mitwirkende',
    }).addTo(map);

    map.on('moveend', () => saveView(map));

    const id = nextId++;
    maps.set(id, { map, dotNetRef, cluster: null, markers: new Map(), color: null });
    return id;
}

export function setPlaces(id, ids, lats, lons, visitedIds, color) {
    const state = maps.get(id);
    if (!state) return;

    if (state.cluster) state.map.removeLayer(state.cluster);
    state.markers.clear();
    state.color = color;

    const visited = new Set(visitedIds);
    const renderer = L.canvas({ padding: 0.5 });
    const cluster = L.markerClusterGroup({
        chunkedLoading: true,
        showCoverageOnHover: false,
        maxClusterRadius: zoom => (zoom < 8 ? 60 : 40),
        // Ab Stadtebene keine Cluster mehr, damit einzelne Filialen sichtbar sind
        disableClusteringAtZoom: 14,
        iconCreateFunction: c => clusterIcon(c, color),
    });

    const markers = ids.map((placeId, i) => {
        const isVisited = visited.has(placeId);
        const marker = L.circleMarker([lats[i], lons[i]], markerStyle(isVisited, color, renderer));
        marker.isVisited = isVisited;
        marker.on('click', () => state.dotNetRef.invokeMethodAsync('OnPlaceClicked', placeId));
        state.markers.set(placeId, marker);
        return marker;
    });

    cluster.addLayers(markers);
    cluster.addTo(state.map);
    state.cluster = cluster;
}

export function setVisited(id, placeId, isVisited) {
    const state = maps.get(id);
    const marker = state?.markers.get(placeId);
    if (!marker) return;
    marker.isVisited = isVisited;
    marker.setStyle(markerStyle(isVisited, state.color));
    state.cluster.refreshClusters(marker);
}

export function flyTo(id, lat, lon, zoom) {
    maps.get(id)?.map.flyTo([lat, lon], zoom ?? 15, { duration: 1.2 });
}

export function showEurope(id) {
    maps.get(id)?.map.flyTo(EUROPE.center, EUROPE.zoom, { duration: 1.2 });
}

export function invalidateSize(id) {
    maps.get(id)?.map.invalidateSize();
}

export function dispose(id) {
    const state = maps.get(id);
    if (!state) return;
    state.map.remove();
    maps.delete(id);
}

// Besucht: gefüllt in Kategoriefarbe. Offen: weiß mit farbigem Ring – hebt sich besser von
// grauen Kartensymbolen (Haltestellen etc.) ab als ein grauer Punkt.
function markerStyle(isVisited, color, renderer) {
    const style = isVisited
        ? { radius: 8, weight: 2, color: '#ffffff', fillColor: color, fillOpacity: 1 }
        : { radius: 6, weight: 3, color, fillColor: '#ffffff', fillOpacity: 1 };
    if (renderer) style.renderer = renderer;
    return style;
}

function clusterIcon(cluster, color) {
    const children = cluster.getAllChildMarkers();
    const count = children.length;
    const visited = children.reduce((n, m) => n + (m.isVisited ? 1 : 0), 0);
    const size = count < 10 ? 32 : count < 100 ? 38 : count < 1000 ? 46 : 54;
    // Kreisring zeigt den Anteil besuchter Orte im Cluster
    const pct = Math.round((visited / count) * 100);
    return L.divIcon({
        html: `<div class="pb-cluster" style="--pb-color:${color};--pb-pct:${pct}%;width:${size}px;height:${size}px">
                 <span>${count}</span></div>`,
        className: 'pb-cluster-wrapper',
        iconSize: L.point(size, size),
    });
}

function loadView() {
    try {
        const v = JSON.parse(localStorage.getItem(VIEW_KEY));
        return Array.isArray(v?.center) && Number.isFinite(v?.zoom) ? v : null;
    } catch {
        return null;
    }
}

function saveView(map) {
    try {
        const c = map.getCenter();
        localStorage.setItem(VIEW_KEY, JSON.stringify({ center: [c.lat, c.lng], zoom: map.getZoom() }));
    } catch {
        // localStorage nicht verfügbar (z. B. privater Modus) – Ansicht wird dann nicht gemerkt
    }
}
