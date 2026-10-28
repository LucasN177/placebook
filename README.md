# 📍 Placebook

Sammle Orte! Placebook zeigt dir auf einer Karte alle Orte einer Kategorie – z. B. alle
**McDonald's-Filialen oder IKEA-Häuser in Europa** – und du hakst ab, wo du schon warst. Die Karte startet mit
ganz Europa und lässt sich bis auf Stadt- und Straßenebene zoomen.

**Live:** https://lucasn177.github.io/placebook/

> Status: Meilensteine M0–M5 umgesetzt (Karte, Liste, Statistik, Standort, Export/Import, PWA, Konto), siehe [Implementationsplan](docs/IMPLEMENTATION_PLAN.md)

## Features

- 🗺️ Interaktive Karte (Leaflet + OpenStreetMap) mit Marker-Clustering
- ✅ Orte als besucht markieren – mit Datum und Notiz
- 🗂️ Kategorien: McDonald's, Burger King, IKEA – die App übernimmt die Farbe der gewählten Kategorie
- 🔎 Listenansicht mit Suche (Stadt, PLZ, Straße, Land) und Sortierung nach Ort, Land oder Entfernung
- 📊 Statistik: Fortschritt gesamt und pro Land, Besuche pro Monat
- 📍 Eigener Standort & nächster noch nicht besuchter Ort
- 💾 Alles bleibt lokal im Browser – Export/Import als JSON-Datei
- 🌙 Light/Dark-Mode, mobilfreundlich
- 📲 Installierbar als App (PWA), App und Ortsdaten funktionieren offline (Kartenkacheln brauchen Internet)

## Tech-Stack

| Bereich | Technologie |
|---|---|
| Frontend | [Blazor WebAssembly](https://learn.microsoft.com/aspnet/core/blazor/) (.NET 10) |
| UI-Komponenten | [MudBlazor](https://mudblazor.com/) |
| Karte | [Leaflet](https://leafletjs.com/) + Leaflet.markercluster via JS-Interop |
| Ortsdaten | Statische JSON-Dateien, generiert aus [OpenStreetMap](https://www.openstreetmap.org/) |
| Speicherung | `localStorage`, optional synchronisiert mit [Supabase](https://supabase.com/) (ohne Login, über Sync-Code) |
| Hosting | GitHub Pages via GitHub Actions |

Es gibt kein Backend und keine Laufzeit-API-Aufrufe für Ortsdaten – alles wird statisch ausgeliefert.

## Projektstruktur

```
src/Placebook/              Blazor WASM App
  wwwroot/data/             Ortsdaten (categories.json + places/<kategorie>.json)
  wwwroot/js/map.js         Leaflet-Interop
  wwwroot/service-worker.published.js   Offline-Cache (PWA)
tools/FetchPlaces/          Tool zum Erzeugen der Ortsdaten aus OSM
tools/generate-icons.mjs    Erzeugt die App-Icons (node tools/generate-icons.mjs)
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

Welche Orte du besucht hast, speichert Placebook zuerst im `localStorage` deines Browsers – die App
funktioniert damit auch offline.

Zusätzlich werden die Besuche in **Supabase** gespeichert (abschaltbar unter *Einstellungen → Cloud-Speicherung*):

- **Kein Login.** Beim ersten Start erzeugt die App einen zufälligen **Sync-Code** (UUID). Alle Besuche liegen
  unter diesem Code; die Datenbank gibt nur Zeilen heraus, deren Code im Header `x-sync-key` mitgeschickt wird
  (Row Level Security). Wer den Code kennt, hat Zugriff – er ist wie ein Passwort zu behandeln.
- **Mehrere Geräte:** Code oder Verbindungslink (`…/settings#sync=<code>`) auf dem anderen Gerät eingeben.
  Die Besuche beider Geräte werden zusammengeführt; pro Ort gewinnt die neueste Änderung.
- **Offline:** Änderungen landen in einer Warteschlange und werden hochgeladen, sobald der Server erreichbar ist.
  Alle zwei Minuten werden außerdem Änderungen anderer Geräte abgeholt.
- **Export/Import** als JSON-Datei bleibt als zusätzliche Sicherung erhalten.

### Konto (optional)

Unter **Konto** kann man sich mit E-Mail und Passwort registrieren und anmelden (Supabase Auth).

- Angemeldet liegen die Besuche im eigenen Konto (Tabelle `account_visits`, abgesichert über `auth.uid()`) und sind
  auf jedem Gerät verfügbar, auf dem man angemeldet ist. Sync-Code und Konto sind strikt getrennt.
- Beim **Anmelden** werden die bisher auf dem Gerät gesammelten Besuche ins Konto übernommen.
- Die Sitzung (Access- und Refresh-Token) liegt im `localStorage` und wird vor Ablauf automatisch erneuert –
  man bleibt angemeldet, bis man sich abmeldet. Ist die Anmeldung endgültig abgelaufen, pausiert die
  Synchronisierung, bis man sich erneut anmeldet.
- Beim **Abmelden** wird zuerst alles hochgeladen, dann werden die Besuche vom Gerät entfernt (sie bleiben im Konto).
- **Passwort vergessen** verschickt eine Mail mit Link; darüber legt man in der App ein neues Passwort fest.

### Supabase einrichten

1. Projekt anlegen, im *SQL Editor* nacheinander einmal ausführen:
   [`001_visits.sql`](supabase/migrations/001_visits.sql) (Sync-Code) und
   [`002_account_visits.sql`](supabase/migrations/002_account_visits.sql) (Konten).
2. *Authentication → URL Configuration*: **Site URL** `https://lucasn177.github.io/placebook/`, unter
   **Redirect URLs** `https://lucasn177.github.io/placebook/**` und `http://localhost:5118/**` eintragen.
   Dorthin führen die Links aus Bestätigungs- und „Passwort vergessen“-Mails zurück.
3. Projekt-URL und **Publishable Key** in [`src/Placebook/wwwroot/appsettings.json`](src/Placebook/wwwroot/appsettings.json)
   eintragen. Der Publishable Key ist öffentlich und darf ins Repo – niemals den Secret/Service-Role-Key verwenden.
4. Ohne Eintrag in `appsettings.json` läuft die App rein lokal.

Hinweis: Kostenlose Supabase-Projekte werden nach längerer Inaktivität pausiert und müssen dann im Dashboard
wieder aktiviert werden; bis dahin arbeitet die App lokal weiter und lädt später nach.

## Lizenz & Attribution

- Ortsdaten und Kartenkacheln: © [OpenStreetMap](https://www.openstreetmap.org/copyright)-Mitwirkende,
  lizenziert unter der [ODbL](https://opendatacommons.org/licenses/odbl/).
- Placebook steht in keiner Verbindung zu McDonald's oder anderen genannten Marken.
