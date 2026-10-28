# 📍 Placebook

Sammle Orte! Placebook zeigt dir auf einer Karte alle Orte einer Kategorie – zum Start alle
**McDonald's-Filialen in Europa** – und du hakst ab, wo du schon warst. Die Karte startet mit
ganz Europa und lässt sich bis auf Stadt- und Straßenebene zoomen.

**Live:** https://lucasn177.github.io/placebook/

> Status: in Entwicklung – Karte und Abhaken funktionieren, siehe [Implementationsplan](docs/IMPLEMENTATION_PLAN.md)

## Features

- 🗺️ Interaktive Karte (Leaflet + OpenStreetMap) mit Marker-Clustering
- ✅ Orte als besucht markieren – mit Datum und Notiz
- 🗂️ Kategorien (McDonald's, weitere folgen)
- 📊 Fortschritt: „123 von 9.800 besucht“ – gesamt und pro Land
- 📍 Eigener Standort & nächster noch nicht besuchter Ort
- 💾 Alles bleibt lokal im Browser – Export/Import als JSON-Datei
- 🌙 Light/Dark-Mode, mobilfreundlich

## Tech-Stack

| Bereich | Technologie |
|---|---|
| Frontend | [Blazor WebAssembly](https://learn.microsoft.com/aspnet/core/blazor/) (.NET 10) |
| UI-Komponenten | [MudBlazor](https://mudblazor.com/) |
| Karte | [Leaflet](https://leafletjs.com/) + Leaflet.markercluster via JS-Interop |
| Ortsdaten | Statische JSON-Dateien, generiert aus [OpenStreetMap](https://www.openstreetmap.org/) |
| Speicherung | `localStorage` |
| Hosting | GitHub Pages via GitHub Actions |

Es gibt kein Backend und keine Laufzeit-API-Aufrufe für Ortsdaten – alles wird statisch ausgeliefert.

## Projektstruktur

```
src/Placebook/              Blazor WASM App
  wwwroot/data/             Ortsdaten (categories.json + places/<kategorie>.json)
  wwwroot/js/map.js         Leaflet-Interop
tools/FetchPlaces/          Tool zum Erzeugen der Ortsdaten aus OSM
tests/Placebook.Tests/      Unit- und Komponententests
docs/                       Planung & Doku
.github/workflows/          Deploy & Daten-Refresh
```

## Lokal starten

Voraussetzung: [.NET 10 SDK](https://dotnet.microsoft.com/download)

```bash
dotnet watch --project src/Placebook
```

Die App läuft dann unter `http://localhost:5118` bzw. `https://localhost:7280`.

**Rider:** Im Ordner [`.run/`](.run) liegen geteilte Run-Configurations, die Rider automatisch anzeigt:
„Placebook“ startet die App (Profil `https`, mit Debugger) und „FetchPlaces (alle Kategorien)“ erzeugt die Ortsdaten neu.

Tests:

```bash
dotnet test
```

## Ortsdaten aktualisieren

Die Orte liegen pro Kategorie als JSON unter `src/Placebook/wwwroot/data/places/`. Sie werden aus
OpenStreetMap über die Overpass API erzeugt:

```bash
dotnet run --project tools/FetchPlaces -- --category mcdonalds
```

Die Abfrage für ganz Europa dauert einige Minuten. Die Rohantwort wird im Temp-Ordner zwischengespeichert;
mit `--from-cache` lässt sie sich erneut auswerten, ohne Overpass neu abzufragen.

Zusätzlich aktualisiert der Workflow [`refresh-data.yml`](.github/workflows/refresh-data.yml) die Daten monatlich
und öffnet einen Pull Request. Dafür muss unter *Settings → Actions → General* die Option
„Allow GitHub Actions to create and approve pull requests“ aktiv sein.

### Neue Kategorie hinzufügen

1. In `tools/categories.config.json` einen Eintrag ergänzen (`id`, `name`, `icon`, `color`, Overpass-`query`).
   Für Ketten am besten über den Wikidata-Brand-Tag filtern, z. B. `["brand:wikidata"="Q38076"]` für McDonald's.
2. `dotnet run --project tools/FetchPlaces -- --category <id>` ausführen.
3. Erzeugte Datei und aktualisierte `categories.json` committen.

Die Orts-IDs entsprechen den OSM-IDs (z. B. `n123456789`) und bleiben bei Aktualisierungen stabil,
damit dein Besucht-Status erhalten bleibt.

## Deployment

Jeder Push auf `main` baut die App und deployt sie per GitHub Actions auf GitHub Pages.

Einmalig einrichten: **Settings → Pages → Build and deployment → Source: GitHub Actions**.

Der Workflow [`deploy.yml`](.github/workflows/deploy.yml) baut und testet die Solution, setzt
`<base href>` auf `/<repo-name>/` und legt `.nojekyll` an. Pull Requests werden nur gebaut, nicht deployt.

Deep Links (z. B. `/placebook/settings`) funktionieren über [`404.html`](src/Placebook/wwwroot/404.html):
GitHub Pages liefert sie für unbekannte Pfade aus, sie leitet auf `/placebook/?/settings` um, und ein
Skript in `index.html` stellt die ursprüngliche URL wieder her, bevor Blazor startet.
Bei einer eigenen Domain muss dort `pathSegmentsToKeep` auf `0` und im Workflow `BASE_HREF` auf `/`
gesetzt werden.

## Deine Daten

Placebook speichert, welche Orte du besucht hast, ausschließlich im `localStorage` deines Browsers.
Es werden keine Daten an einen Server übertragen. Wechselst du Gerät oder Browser, nutze
**Einstellungen → Exportieren / Importieren**.

## Lizenz & Attribution

- Ortsdaten und Kartenkacheln: © [OpenStreetMap](https://www.openstreetmap.org/copyright)-Mitwirkende,
  lizenziert unter der [ODbL](https://opendatacommons.org/licenses/odbl/).
- Placebook steht in keiner Verbindung zu McDonald's oder anderen genannten Marken.
