# Placebook – Implementationsplan

Placebook ist eine rein clientseitige Web-App (Blazor WebAssembly + MudBlazor), in der man auf einer Karte
Orte einer Kategorie (z. B. McDonald's-Filialen) sieht und als „besucht“ markieren kann. Gehostet wird
statisch über GitHub Pages – es gibt **kein Backend**.

---

## 1. Ziele & Rahmenbedingungen

| Thema | Entscheidung |
|---|---|
| Hosting | GitHub Pages (`https://lucasn177.github.io/placebook/`), rein statisch |
| Framework | .NET 10 (LTS), Blazor WebAssembly Standalone |
| UI | MudBlazor 8.x |
| Karte | Leaflet.js über eigenes, schlankes JS-Interop-Modul + Leaflet.markercluster |
| Kartenkacheln | OpenStreetMap-Standardkacheln (Attribution Pflicht) |
| Ortsdaten | Statische JSON-Dateien pro Kategorie unter `wwwroot/data/` |
| Datenquelle | OpenStreetMap (Overpass API), **einmalig/periodisch per Skript** erzeugt – nie zur Laufzeit |
| Besucht-Status | `localStorage` im Browser + Export/Import als JSON-Datei |
| Startumfang | Kategorie „McDonald's“, Region **Europa** (Startansicht ganz Europa, zoombar bis auf Stadt-/Straßenebene) |
| Besuche | Pro Ort nur „besucht ja/nein“ + optional Datum/Notiz – **keine Mehrfachbesuche** |

**Nicht-Ziele (MVP):** Benutzerkonten, Server-Sync, Bearbeiten von Ortsdaten in der App.

### Warum Leaflet statt eines Blazor-Map-Pakets?
Die verfügbaren Blazor-Wrapper für Leaflet sind teils unmaintained. Ein eigenes Interop-Modul mit
~5 Funktionen (`init`, `setMarkers`, `updateMarker`, `flyTo`, `dispose`) ist schnell geschrieben, voll
kontrollierbar und hält die Abhängigkeiten klein. Marker-Rendering findet komplett in JS statt – bei
~1.500 Filialen wäre das Anlegen einzelner Marker über Interop-Calls zu langsam.

---

## 2. Architektur

```
┌──────────────────────── Browser ────────────────────────┐
│  Blazor WASM (C#)                                       │
│   ├─ Pages: Map, List, Stats, Settings                  │
│   ├─ CategoryService   ──HTTP GET──► data/*.json        │
│   ├─ VisitService      ──Interop──►  localStorage       │
│   └─ MapInterop        ──Interop──►  wwwroot/js/map.js  │
│                                         └─ Leaflet      │
└─────────────────────────────────────────────────────────┘
          ▲ statische Dateien
   GitHub Pages  ◄── GitHub Actions (build + deploy)
                 ◄── GitHub Actions (monatlicher Daten-Refresh → PR)
```

### Projektstruktur

```
placebook/
├─ .github/workflows/
│  ├─ deploy.yml               # Build & Deploy nach GitHub Pages
│  └─ refresh-data.yml         # Monatlich Daten neu ziehen, PR erstellen
├─ docs/
│  └─ IMPLEMENTATION_PLAN.md
├─ src/Placebook/              # Blazor WASM Projekt
│  ├─ Program.cs
│  ├─ App.razor / Routes.razor
│  ├─ Layout/MainLayout.razor
│  ├─ Pages/
│  │  ├─ MapPage.razor         # "/"
│  │  ├─ ListPage.razor        # "/list"
│  │  ├─ StatsPage.razor       # "/stats"
│  │  └─ SettingsPage.razor    # "/settings" (Export/Import, Theme)
│  ├─ Components/
│  │  ├─ LeafletMap.razor      # Kapselt das JS-Interop
│  │  ├─ CategorySelect.razor
│  │  └─ PlaceDetailsDrawer.razor
│  ├─ Models/                  # Category, Place, Visit, VisitStore
│  ├─ Services/                # CategoryService, VisitService, MapInterop
│  └─ wwwroot/
│     ├─ index.html            # <base href="/" /> wird im CI ersetzt
│     ├─ 404.html              # SPA-Fallback für GitHub Pages
│     ├─ js/map.js
│     ├─ lib/leaflet/…         # Leaflet + markercluster lokal (kein CDN-Zwang)
│     └─ data/
│        ├─ categories.json
│        └─ places/
│           └─ mcdonalds.json
├─ tools/
│  ├─ categories.config.json   # Overpass-Queries pro Kategorie
│  └─ FetchPlaces/             # .NET Konsolen-Tool, erzeugt data/places/*.json
├─ tests/Placebook.Tests/      # bUnit + xUnit
├─ Placebook.sln
└─ README.md
```

---

## 3. Datenmodell

### 3.1 `wwwroot/data/categories.json` (Index)

```json
[
  {
    "id": "mcdonalds",
    "name": "McDonald's",
    "icon": "fastfood",
    "color": "#DA291C",
    "file": "places/mcdonalds.json",
    "count": 1412,
    "updatedAt": "2026-10-28"
  }
]
```

### 3.2 `wwwroot/data/places/<kategorie>.json`

Bewusst kompakt (kurze Keys, keine Nullwerte), damit die Datei klein bleibt. GitHub Pages liefert
gzip-komprimiert aus.

```json
{
  "category": "mcdonalds",
  "source": "© OpenStreetMap contributors, ODbL",
  "generatedAt": "2026-10-28T10:00:00Z",
  "places": [
    { "id": "n123456789", "n": "McDonald's", "lat": 52.5200, "lon": 13.4050,
      "a": "Alexanderplatz 1", "c": "Berlin", "z": "10178", "cc": "DE" }
  ]
}
```

- `id` = OSM-Typ-Präfix (`n`/`w`/`r`) + OSM-ID → **stabil** über Daten-Refreshes hinweg.
  Das ist wichtig, weil der Besucht-Status über diese ID referenziert wird.
- Ways/Relations (Gebäude-Umrisse) werden beim Export auf ihren Mittelpunkt (`out center`) reduziert.

### 3.3 Besucht-Status (localStorage, Key `placebook.visits.v1`)

```json
{
  "version": 1,
  "visits": {
    "mcdonalds": {
      "n123456789": { "visitedAt": "2026-10-20", "note": "Bester McFlurry" }
    }
  }
}
```

- Versioniertes Schema → spätere Migrationen möglich.
- Verwaiste IDs (Filiale geschlossen/aus OSM entfernt) werden **nicht** gelöscht, sondern in der
  Statistik separat als „nicht mehr vorhanden“ angezeigt.

---

## 4. Datenbeschaffung (Tool `tools/FetchPlaces`)

Kleines .NET-Konsolenprogramm, das pro Kategorie eine Overpass-Query ausführt und die kompakte JSON
schreibt.

Beispiel-Query McDonald's Europa (über Wikidata-Brand-Tag, zuverlässiger als der Name). Europa wird
über eine Bounding Box (Süd, West, Nord, Ost) abgefragt; Orte außerhalb Europas (Nordafrika, Nahost)
filtert das Tool anschließend über `addr:country` bzw. eine Länderliste heraus:

```
[out:json][timeout:600];
nwr["brand:wikidata"="Q38076"]["amenity"="fast_food"](34.0,-25.0,71.5,45.0);
out center tags;
```

Erwartete Größe: ca. 9.000–10.000 Filialen → im kompakten Format ca. 1–1,5 MB JSON, gzip-komprimiert
ca. 250–350 KB. Das ist als eine Datei pro Kategorie gut handhabbar. Pro Ort wird zusätzlich der
Ländercode (`cc`) gespeichert, für Filter und Statistik pro Land.

`tools/categories.config.json` definiert pro Kategorie: `id`, `name`, `icon`, `color`, `query`.
Neue Kategorien (Burger King `Q177054`, IKEA `Q54078`, Zoos, Freizeitparks, …) brauchen damit nur einen
neuen Config-Eintrag + Tool-Lauf.

Aufruf:

```bash
dotnet run --project tools/FetchPlaces -- --category mcdonalds
```

Der Workflow `refresh-data.yml` führt das monatlich aus und öffnet bei Änderungen einen Pull Request.

---

## 5. Features

### MVP (Meilenstein 1–4)
- Karte (Vollbild), Startansicht Europa (Zentrum ca. 50° N / 10° O, Zoom 4), zoombar bis Zoom 18
- Alle Orte der gewählten Kategorie mit Marker-Clustering (Cluster lösen sich beim Reinzoomen bis auf Stadtebene auf)
- Marker-Farbe: besucht (Kategoriefarbe, gefüllt) vs. offen (grau)
- Klick auf Marker → Detail-Drawer: Name, Adresse, „Besucht“-Toggle, Datum, Notiz
- Kategorie-Auswahl in der AppBar
- Filter: alle / besucht / offen
- Fortschrittsanzeige „123 / 1.412 besucht (8,7 %)“
- „Meinen Standort zeigen“ (Geolocation API) + „Nächste offene Filiale“
- Export/Import des Besucht-Status als JSON-Datei
- Light/Dark-Mode

### Danach (Backlog)
- Listenansicht mit Suche (Stadt, PLZ) und Sortierung nach Entfernung
- Statistik-Seite: pro Land/Stadt (MudBlazor Charts)
- PWA (offline nutzbar, installierbar) – Blazor-Template bringt Service Worker mit
- Weitere Kategorien; bei weltweiten Daten Aufteilung der JSON pro Region
- Optionaler Sync über GitHub Gist (Personal Access Token im Browser) – bewusst nicht im MVP
- Teilen-Link mit komprimiertem Besucht-Status im URL-Fragment (`#…`)

---

## 6. GitHub Pages Deployment

Besonderheiten, die bei Blazor WASM auf GitHub Pages beachtet werden müssen:

1. **Base-Pfad:** Die App liegt unter `/placebook/`. Im CI wird `<base href="/" />` in `index.html`
   durch `<base href="/placebook/" />` ersetzt. Lokal bleibt `/`.
2. **`.nojekyll`:** Sonst ignoriert Jekyll Ordner mit `_` (z. B. `_framework`).
3. **SPA-Routing / Deep Links:** GitHub Pages kennt keine Rewrites. Lösung: `404.html`, die den Pfad
   in einen Query-Parameter umschreibt und auf `index.html` umleitet; ein kleines Skript in
   `index.html` stellt die URL wieder her (bekanntes „spa-github-pages“-Muster).
4. **Deploy via Actions** (`actions/upload-pages-artifact` + `actions/deploy-pages`), kein `gh-pages`-Branch nötig.
   In den Repo-Einstellungen: *Settings → Pages → Source: GitHub Actions*.

Skizze `deploy.yml`:

```yaml
name: Deploy
on:
  push: { branches: [main] }
  workflow_dispatch:
permissions: { contents: read, pages: write, id-token: write }
concurrency: { group: pages, cancel-in-progress: true }
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.x' }
      - run: dotnet test
      - run: dotnet publish src/Placebook -c Release -o out
      - run: |
          sed -i 's|<base href="/" />|<base href="/placebook/" />|' out/wwwroot/index.html
          touch out/wwwroot/.nojekyll
      - uses: actions/upload-pages-artifact@v3
        with: { path: out/wwwroot }
  deploy:
    needs: build
    runs-on: ubuntu-latest
    environment: { name: github-pages, url: '${{ steps.d.outputs.page_url }}' }
    steps:
      - id: d
        uses: actions/deploy-pages@v4
```

---

## 7. Meilensteine

### M0 – Projekt-Setup (½ Tag)
- [x] `dotnet new blazorwasm -o src/Placebook` (ohne Sample-Seiten), Solution anlegen
- [x] MudBlazor einbinden (`MudThemeProvider`, `MudPopoverProvider`, `MudDialogProvider`, `MudSnackbarProvider`)
- [x] `.gitignore` (inkl. `.idea/`)
- [x] `deploy.yml`, `404.html`-Fallback, Base-Pfad-Anpassung
- [ ] Pages-Einstellungen (Source: GitHub Actions) → leere App läuft unter `lucasn177.github.io/placebook`
- **Done, wenn:** Push auf `main` deployt automatisch, Deep-Link-Reload funktioniert.

### M1 – Daten (½–1 Tag)
- [ ] `tools/FetchPlaces` + `categories.config.json`
- [ ] `mcdonalds.json` für Europa generieren und committen
- [ ] `CategoryService`: lädt `categories.json` und lazy die Ortsdatei der aktiven Kategorie (Cache im Speicher)
- **Done, wenn:** Unit-Test lädt/deserialisiert die echte Datei, IDs sind eindeutig.

### M2 – Karte (1–2 Tage)
- [ ] Leaflet + markercluster nach `wwwroot/lib` legen, `map.js` als ES-Modul
- [ ] `LeafletMap.razor`: `init`, `setMarkers(places, visitedIds)`, `setVisited(id, bool)`, `flyTo`, Marker-Klick → `DotNetObjectReference` Callback
- [ ] Kartenausschnitt (Zentrum/Zoom) in localStorage merken
- **Done, wenn:** alle Filialen performant angezeigt werden (< 1 s Render), Klick öffnet Drawer.

### M3 – Besucht markieren (1 Tag)
- [ ] `VisitService` (laden, speichern, Event `VisitsChanged`)
- [ ] Detail-Drawer mit Toggle, Datum (MudDatePicker), Notiz
- [ ] Fortschrittsanzeige + Filter alle/besucht/offen
- **Done, wenn:** Status überlebt Reload, Marker färbt sich sofort um.

### M4 – Komfort & Release (1 Tag)
- [ ] Export/Import (Download via JS-Blob, Upload via `MudFileUpload`), Import mit Merge-Option
- [ ] Geolocation + „nächste offene Filiale“
- [ ] Dark Mode, responsive Layout (mobil: Drawer von unten)
- [ ] OSM-Attribution sichtbar, Impressum/Datenquelle in Settings
- [ ] `refresh-data.yml`
- **Done, wenn:** v1.0 getaggt, auf dem Handy benutzbar.

### M5+ – Backlog (siehe Abschnitt 5)

---

## 8. Tests

- **xUnit:** Datenmodell, Serialisierung, Migration des Visit-Schemas, Entfernungsberechnung (Haversine).
- **bUnit:** Komponenten (Drawer, Fortschritt, Kategorie-Auswahl) mit gemocktem `IJSRuntime`.
- **Manuell/Smoke:** Nach Deploy Karte laden, Ort markieren, Reload, Deep-Link `/stats` neu laden.

---

## 9. Risiken & offene Punkte

| Risiko | Gegenmaßnahme |
|---|---|
| Dateigröße (Europa ≈ 10.000 Orte, weltweit ≈ 40.000) | Europa passt in eine Datei; bei weltweit Aufteilung pro Region (`places/mcdonalds/eu.json`) |
| Performance bei ~10.000 Markern | Leaflet.markercluster mit `chunkedLoading`, `circleMarker` auf Canvas-Renderer |
| Erste Ladezeit Blazor WASM (~2–4 MB) | AOT nicht nötig; Trimming aktiv, Ladeanimation in `index.html`, Brotli/gzip durch Pages |
| OSM-Tile-Nutzungsrichtlinie | Für privates Projekt ok; bei mehr Traffic auf anderen Anbieter (z. B. MapTiler, Stadia) mit Key wechseln |
| Datenverlust (localStorage gelöscht / anderes Gerät) | Export/Import prominent anbieten, später optional Gist-Sync |
| Unvollständige OSM-Daten | Hinweis in der App; fehlende Filialen kann man in OSM selbst ergänzen |
| Lizenz | Ortsdaten ODbL → Attribution in App & README |

**Entschieden:**
- Region: Europa
- Keine Mehrfachbesuche – ein Ort ist besucht oder nicht (optional mit Datum und Notiz)

**Offen:**
- Eigene Domain statt `github.io`?
