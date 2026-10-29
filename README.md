# 📍 Placebook

Sammle Orte! Placebook zeigt dir auf einer Karte alle Orte einer Kategorie – z. B. alle
**McDonald's-Filialen oder IKEA-Häuser in Europa** – und du hakst ab, wo du schon warst. Die Karte startet mit
ganz Europa und lässt sich bis auf Stadt- und Straßenebene zoomen.

**Live:** https://lucasn177.github.io/placebook/

> Status: Version 1.1.0 – Karte, Liste, Statistik, Standort, Export/Import, PWA, Cloud-Speicherung und Konten.
> Details und Backlog im [Implementationsplan](docs/IMPLEMENTATION_PLAN.md).

## Features

- 🗺️ Interaktive Karte (Leaflet + OpenStreetMap) mit Marker-Clustering; der Ring um jedes Cluster zeigt den Anteil besuchter Orte
- ✅ Orte als besucht markieren – mit Datum und Notiz
- 🗂️ Kategorien wählbar in der Kopfzeile; die App übernimmt die Farbe der gewählten Kategorie
- 🔎 Listenansicht mit Suche (Stadt, PLZ, Straße, Land) und Sortierung nach Ort, Land oder Entfernung
- 🏳️ Länderansicht: ein Klick auf ein Land (in der Liste oder Statistik) zeigt alle Orte dieses Landes mit Fortschritt und Besucht-Markierung (`/list/AT`)
- 📊 Statistik: Fortschritt gesamt und pro Land, Besuche pro Monat
- 📍 Eigener Standort & nächster noch nicht besuchter Ort
- ☁️ Besuche in der Cloud (Supabase) – ohne Anmeldung über einen Sync-Code oder mit Konto (E-Mail + Passwort)
- 💾 Offline-first: alles liegt zuerst lokal im Browser; zusätzlich Export/Import als JSON-Datei
- 🌙 Light/Dark-Mode, mobilfreundlich (Detailansicht fährt auf dem Handy von unten ein)
- 📲 Installierbar als App (PWA), App und Ortsdaten funktionieren offline (Kartenkacheln brauchen Internet)

### Kategorien

| Kategorie | Orte | Länder | Quelle (OpenStreetMap) |
|---|---:|---:|---|
| McDonald's | 8.783 | 37 | `brand:wikidata=Q38076` |
| Burger King | 4.318 | 37 | `brand:wikidata=Q177054` |
| IKEA | 424 | 31 | `brand:wikidata=Q54078` |
| Krankenhäuser | 2.850 | 10 von 45 | `amenity=hospital` – *Teildaten, weitere Länder folgen* |
| Universitäten | 1.929 | 26 von 45 | `amenity=university` mit `wikidata` – *Teildaten, weitere Länder folgen* |

Bei Teildaten zeigt die App einen Hinweis mit den noch fehlenden Ländern.

Region: 45 europäische Länder (EU, EWR, Schweiz, Vereinigtes Königreich, Westbalkan, Ukraine, Moldau, Belarus u. a.).

## Tech-Stack

| Bereich | Technologie |
|---|---|
| Frontend | [Blazor WebAssembly](https://learn.microsoft.com/aspnet/core/blazor/) (.NET 10) |
| UI-Komponenten | [MudBlazor](https://mudblazor.com/) 9 |
| Karte | [Leaflet](https://leafletjs.com/) 1.9 + Leaflet.markercluster via JS-Interop |
| Ortsdaten | Statische JSON-Dateien, generiert aus [OpenStreetMap](https://www.openstreetmap.org/) über die Overpass API |
| Speicherung | `localStorage`, synchronisiert mit [Supabase](https://supabase.com/) (Postgres + Row Level Security) |
| Anmeldung | Supabase Auth (E-Mail + Passwort), direkt über die REST-Schnittstelle |
| Hosting | GitHub Pages via GitHub Actions |

Die App ist rein statisch: Ortsdaten werden mit ausgeliefert, nur Besuche und Anmeldung laufen über Supabase.
Ohne Supabase-Konfiguration funktioniert sie vollständig lokal.

## Projektstruktur

```
src/Placebook/                     Blazor WASM App
  Pages/                           Karte, Liste, Statistik, Einstellungen, Konto
  Components/                      Karte (Leaflet), Sync-Einstellungen, Konto-Dialoge
  Services/                        Ortsdaten, Besuche, Synchronisierung, Supabase-Client
  Services/Auth/                   Anmeldung (Supabase Auth)
  wwwroot/appsettings.json         Supabase-URL und Publishable Key
  wwwroot/data/                    Ortsdaten (categories.json + places/<kategorie>.json)
  wwwroot/js/map.js                Leaflet-Interop
  wwwroot/js/version.js            Versionsnummer (einzige Quelle)
  wwwroot/service-worker.published.js   Offline-Cache (PWA)
supabase/migrations/               SQL für Tabellen und Zugriffsregeln
tools/FetchPlaces/                 Tool zum Erzeugen der Ortsdaten aus OSM
tools/categories.config.json       Kategorien und ihre Overpass-Filter
tools/generate-icons.mjs           Erzeugt die App-Icons (node tools/generate-icons.mjs)
tests/Placebook.Tests/             Unit-Tests (Daten, Suche, Statistik, Sync, Anmeldung)
docs/                              Planung & Doku
.github/workflows/                 Deploy & monatlicher Daten-Refresh
.run/                              Run-Configurations für Rider
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
dotnet run --project tools/FetchPlaces -- --category mcdonalds --from-cache
```

- Ohne `--category` werden alle Kategorien abgefragt. Scheitert eine, laufen die übrigen trotzdem durch.
- Die öffentlichen Overpass-Server sind oft überlastet. Das Tool probiert deshalb drei Server nacheinander,
  jeweils mit Wiederholung, und überspringt einen Server für den Rest des Laufs, wenn er wiederholt versagt.
- Rohantworten werden im Temp-Ordner zwischengespeichert. `--from-cache` verwendet sie wieder – bei Kategorien,
  die Land für Land abgefragt werden, setzt ein neuer Lauf so beim ersten fehlenden Land fort.
- Eine Ortsdatei wird erst geschrieben, wenn alle Länder einer Kategorie vorliegen. Ausnahme: `--cache-only`
  erzeugt sie ohne neue Abfragen aus den bereits zwischengespeicherten Ländern und vermerkt die fehlenden in
  `categories.json` (Teildaten).

Zusätzlich aktualisiert der Workflow [`refresh-data.yml`](.github/workflows/refresh-data.yml) die Daten monatlich
und öffnet einen Pull Request. Dafür muss unter *Settings → Actions → General* die Option
„Allow GitHub Actions to create and approve pull requests“ aktiv sein.

### Neue Kategorie hinzufügen

1. In [`tools/categories.config.json`](tools/categories.config.json) einen Eintrag ergänzen: `id`, `name`, `icon`
   (Material-Icon-Name), `color` und den Overpass-`filter`.
   Für Ketten am besten über den Wikidata-Brand-Tag filtern, z. B. `nwr["brand:wikidata"="Q38076"]` für McDonald's.
   Optional:
   - `"perCountry": true` fragt Land für Land ab – für häufige Tags wie Krankenhäuser, die als eine
     Europa-Abfrage zu schwer für die Server sind.
   - `"mergeSameName": true` fasst Einträge mit gleichem Namen im selben Land und im Umkreis von 1,5 km zusammen
     (z. B. Uni-Campus und einzelne Gebäude).
   - `"dedupeTag": "wikidata"` übernimmt nur Einträge mit diesem Tag und behält pro Wert einen – bei Universitäten
     trennt das eigenständige Hochschulen von Instituten, Fakultäten und Gebäuden.
   - `"itemName"` benennt Orte ohne eigenen Namen (z. B. „Krankenhaus“).
2. Ein neues Icon in [`CategoryIcons.cs`](src/Placebook/Models/CategoryIcons.cs) eintragen.
3. `dotnet run --project tools/FetchPlaces -- --category <id> --from-cache` ausführen.
4. Erzeugte Datei und aktualisierte `categories.json` committen.

Die Orts-IDs entsprechen den OSM-IDs (z. B. `n123456789`) und bleiben bei Aktualisierungen stabil,
damit dein Besucht-Status erhalten bleibt.

## Deployment

Jeder Push auf `main` baut die App und deployt sie per GitHub Actions auf GitHub Pages.

Einmalig einrichten: **Settings → Pages → Build and deployment → Source: GitHub Actions**.

Der Workflow [`deploy.yml`](.github/workflows/deploy.yml) baut und testet die Solution, setzt
`<base href>` auf `/<repo-name>/`, aktualisiert danach den Hash von `index.html` im Service-Worker-Manifest
(sonst schlägt die Offline-Installation fehl) und legt `.nojekyll` an. Pull Requests werden nur gebaut, nicht deployt.

Deep Links (z. B. `/placebook/settings`) funktionieren über [`404.html`](src/Placebook/wwwroot/404.html):
GitHub Pages liefert sie für unbekannte Pfade aus, sie leitet auf `/placebook/?/settings` um, und ein
Skript in `index.html` stellt die ursprüngliche URL wieder her, bevor Blazor startet.
Bei einer eigenen Domain muss dort `pathSegmentsToKeep` auf `0` und im Workflow `BASE_HREF` auf `/`
gesetzt werden.

### Neue Version veröffentlichen

1. Versionsnummer in [`src/Placebook/wwwroot/js/version.js`](src/Placebook/wwwroot/js/version.js) erhöhen – sie
   erscheint im Ladebildschirm und unten im Menü.
2. Auf `main` pushen; optional ein Tag setzen (`git tag -a v1.1.0 -m "…" && git push origin v1.1.0`).

Installierte Apps holen sich die neue Version beim nächsten Öffnen; ggf. einmal neu laden.

## Deine Daten

Welche Orte du besucht hast, speichert Placebook zuerst im `localStorage` deines Browsers – die App
funktioniert damit auch offline. Zusätzlich werden die Besuche in **Supabase** gespeichert, auf einem von zwei Wegen:

### Ohne Anmeldung: Sync-Code

(abschaltbar unter *Einstellungen → Cloud-Speicherung*)

- Beim ersten Start erzeugt die App einen zufälligen **Sync-Code** (UUID). Alle Besuche liegen unter diesem Code;
  die Datenbank gibt nur Zeilen heraus, deren Code im Header `x-sync-key` mitgeschickt wird (Row Level Security).
  Wer den Code kennt, hat Zugriff – er ist wie ein Passwort zu behandeln.
- **Mehrere Geräte:** Code oder Verbindungslink (`…/settings#sync=<code>`) auf dem anderen Gerät eingeben.
  Die Besuche beider Geräte werden zusammengeführt.

### Mit Konto: E-Mail und Passwort

Unter **Konto** (Symbol oben rechts) kann man sich registrieren und anmelden (Supabase Auth).

- Angemeldet liegen die Besuche im eigenen Konto (Tabelle `account_visits`, abgesichert über `auth.uid()`) und sind
  auf jedem Gerät verfügbar, auf dem man angemeldet ist. Sync-Code und Konto sind strikt getrennt.
- Beim **Anmelden** werden die bisher auf dem Gerät gesammelten Besuche ins Konto übernommen.
- Die Sitzung (Access- und Refresh-Token) liegt im `localStorage` und wird vor Ablauf automatisch erneuert –
  man bleibt angemeldet, bis man sich abmeldet. Ist die Anmeldung endgültig abgelaufen, pausiert die
  Synchronisierung, bis man sich erneut anmeldet.
- Beim **Abmelden** wird zuerst alles hochgeladen, dann werden die Besuche vom Gerät entfernt (sie bleiben im Konto).
- **Passwort vergessen** verschickt eine Mail mit Link; darüber legt man in der App ein neues Passwort fest.

### So funktioniert die Synchronisierung

- **Offline-first:** Änderungen landen sofort lokal und in einer Warteschlange; sie werden gesammelt hochgeladen,
  sobald der Server erreichbar ist. Beim Start und alle zwei Minuten werden Änderungen anderer Geräte abgeholt.
- **Konflikte:** Pro Ort gewinnt die neueste Änderung („last write wins“). Ein Datenbank-Trigger stellt sicher, dass
  ein älterer Stand nie einen neueren überschreibt.
- **Löschen:** Entfernte Besuche werden als gelöscht markiert statt entfernt, damit die Löschung alle Geräte erreicht.
- **Export/Import** als JSON-Datei bleibt als zusätzliche Sicherung erhalten.

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

## Technische Hinweise

- **JSON in WebAssembly:** Ortsdateien werden mit dem System.Text.Json-Source-Generator (`PlacebookJsonContext`)
  gelesen und vorher komplett als String geladen. Reflection und das gestreamte `GetFromJsonAsync` waren im
  WebAssembly-Interpreter um ein Vielfaches langsamer (≈ 25 s statt ≈ 1 s für die McDonald's-Datei).
- **Marker:** Alle Marker erzeugt `map.js` in einem Rutsch als Canvas-Kreise; Blazor übergibt dafür nur drei
  einfache Arrays (IDs, Breiten, Längen).

## Lizenz & Attribution

- Placebook – by Lucas Nagelsmann.
- Ortsdaten und Kartenkacheln: © [OpenStreetMap](https://www.openstreetmap.org/copyright)-Mitwirkende,
  lizenziert unter der [ODbL](https://opendatacommons.org/licenses/odbl/).
- Leaflet und Leaflet.markercluster stehen unter BSD-2-Clause- bzw. MIT-Lizenz (siehe `wwwroot/lib/*/LICENSE`).
- Placebook steht in keiner Verbindung zu McDonald's, Burger King, IKEA oder anderen genannten Marken.
