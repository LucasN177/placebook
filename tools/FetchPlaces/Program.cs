// Erzeugt die statischen Ortsdaten (wwwroot/data) aus OpenStreetMap über die Overpass API.
//
//   dotnet run --project tools/FetchPlaces                         # alle Kategorien
//   dotnet run --project tools/FetchPlaces -- --category mcdonalds # nur eine Kategorie
//   dotnet run --project tools/FetchPlaces -- --from-cache         # letzte Overpass-Antwort erneut auswerten

using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FetchPlaces;

var repoRoot = FindRepoRoot();
var configPath = Path.Combine(repoRoot, "tools", "categories.config.json");
var dataDir = Path.Combine(repoRoot, "src", "Placebook", "wwwroot", "data");

var config = JsonSerializer.Deserialize<ToolConfig>(File.ReadAllText(configPath), Json.Read)
             ?? throw new InvalidOperationException($"Konfiguration {configPath} ist leer.");

var onlyCategory = GetArg("--category");
var categories = config.Categories
    .Where(c => onlyCategory is null || c.Id == onlyCategory)
    .ToList();
if (categories.Count == 0)
{
    Console.Error.WriteLine($"Unbekannte Kategorie '{onlyCategory}'.");
    return 1;
}

// Timeout pro Anfrage steuert OverpassClient (Europa-weit länger als pro Land)
using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
http.DefaultRequestHeaders.UserAgent.ParseAdd("Placebook-FetchPlaces/1.0 (+https://github.com/LucasN177/placebook)");
var overpass = new OverpassClient(http);

var indexPath = Path.Combine(dataDir, "categories.json");
var failed = new List<string>();

foreach (var category in categories)
{
    Console.WriteLine($"[{category.Id}] Frage Overpass ab …");
    List<Place> places;
    try
    {
        places = await overpass.FetchPlacesAsync(category, config.Region, args.Contains("--from-cache"));
    }
    catch (InvalidOperationException ex)
    {
        // Eine fehlgeschlagene Kategorie soll die übrigen nicht verhindern
        Console.Error.WriteLine($"[{category.Id}] übersprungen: {ex.Message}");
        failed.Add(category.Id);
        continue;
    }
    Console.WriteLine($"[{category.Id}] {places.Count} Orte in {places.Select(p => p.Cc).Distinct().Count()} Ländern");

    var file = $"places/{category.Id}.json";
    var path = Path.Combine(dataDir, "places", $"{category.Id}.json");
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, PlacesWriter.Write(category, places, DateTimeOffset.UtcNow), new UTF8Encoding(false));

    // Index direkt vor dem Schreiben neu einlesen: Abfragen dauern Minuten, parallel laufende
    // Aufrufe für andere Kategorien sollen sich nicht gegenseitig überschreiben.
    var index = LoadIndex(indexPath);
    index.RemoveAll(c => c.Id == category.Id);
    index.Add(new CategoryIndexEntry(category.Id, category.Name, category.Icon, category.Color, file,
        places.Count, DateOnly.FromDateTime(DateTime.UtcNow)));
    // Reihenfolge aus der Config beibehalten
    index = index.OrderBy(c => config.Categories.FindIndex(x => x.Id == c.Id)).ToList();
    File.WriteAllText(indexPath, JsonSerializer.Serialize(index, Json.WriteIndented) + "\n", new UTF8Encoding(false));
}

if (failed.Count > 0)
{
    Console.Error.WriteLine($"Fehlgeschlagen: {string.Join(", ", failed)}");
    return 1;
}
Console.WriteLine("Fertig.");
return 0;

string? GetArg(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Placebook.sln")))
        dir = dir.Parent;
    return dir?.FullName ?? throw new InvalidOperationException("Placebook.sln nicht gefunden – Tool im Repo ausführen.");
}

static List<CategoryIndexEntry> LoadIndex(string path) =>
    File.Exists(path)
        ? JsonSerializer.Deserialize<List<CategoryIndexEntry>>(File.ReadAllText(path), Json.Read) ?? []
        : [];

namespace FetchPlaces
{
    record ToolConfig(Region Region, List<CategoryConfig> Categories);

    record Region(string Name, BoundingBox Bbox, List<string> Countries);

    record BoundingBox(double South, double West, double North, double East)
    {
        public bool Contains(double lat, double lon) => lat >= South && lat <= North && lon >= West && lon <= East;
    }

    /// <param name="PerCountry">Eine Overpass-Abfrage pro Land statt einer für ganz Europa. Für häufige Tags
    /// (z. B. Krankenhäuser) ist die Gesamtabfrage für die öffentlichen Server zu schwer.</param>
    /// <param name="MergeSameName">Einträge mit gleichem Namen im selben Land und weniger als 1,5 km Abstand
    /// zusammenfassen – z. B. sind bei Universitäten oft Campus und einzelne Gebäude getaggt.</param>
    record CategoryConfig(string Id, string Name, string Icon, string Color, string Filter,
        bool PerCountry = false, bool MergeSameName = false);

    record CategoryIndexEntry(string Id, string Name, string Icon, string Color, string File, int Count, DateOnly UpdatedAt);

    /// <summary>Kompaktes Ortsformat, siehe docs/IMPLEMENTATION_PLAN.md, Abschnitt 3.2.</summary>
    record Place(string Id, string? N, double Lat, double Lon, string? A, string? C, string? Z, string Cc);

    static class Json
    {
        public static readonly JsonSerializerOptions Read = new(JsonSerializerDefaults.Web);

        public static readonly JsonSerializerOptions WriteIndented = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static readonly JsonSerializerOptions WriteCompact = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }

    class OverpassClient(HttpClient http)
    {
        private static readonly string[] Endpoints =
        [
            "https://overpass-api.de/api/interpreter",
            "https://maps.mail.ru/osm/tools/overpass/api/interpreter",
            "https://overpass.private.coffee/api/interpreter"
        ];

        private static readonly TimeSpan RegionTimeout = TimeSpan.FromMinutes(20);
        private static readonly TimeSpan CountryTimeout = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Fragt Overpass ab. Die Rohantwort wird im Temp-Ordner zwischengespeichert; mit <paramref name="fromCache"/>
        /// wird sie wiederverwendet, statt die minutenlange Abfrage erneut zu starten.
        /// </summary>
        public async Task<List<Place>> FetchPlacesAsync(CategoryConfig category, Region region, bool fromCache)
        {
            var places = category.PerCountry
                ? await FetchPerCountryAsync(category, region, fromCache)
                : await FetchPartAsync(category, region, $"placebook-overpass-{category.Id}.json", fromCache, RegionTimeout);
            if (category.MergeSameName)
            {
                var merged = PlaceMerger.MergeSameName(places);
                Console.WriteLine($"[{category.Id}] {places.Count - merged.Count} Duplikate mit gleichem Namen zusammengefasst");
                places = merged;
            }
            return places;
        }

        private async Task<List<Place>> FetchPerCountryAsync(CategoryConfig category, Region region, bool fromCache)
        {
            // Pro Land einzeln abfragen und zwischenspeichern – mit --from-cache setzt ein neuer Lauf bei den
            // fehlenden Ländern fort, statt von vorn zu beginnen
            var places = new Dictionary<string, Place>();
            for (var i = 0; i < region.Countries.Count; i++)
            {
                var country = region.Countries[i];
                Console.WriteLine($"[{category.Id}] {country} ({i + 1}/{region.Countries.Count})");
                var part = await FetchPartAsync(category, region with { Countries = [country] },
                    $"placebook-overpass-{category.Id}-{country}.json", fromCache, CountryTimeout);
                foreach (var place in part)
                    places.TryAdd(place.Id, place);
            }
            return places.Values.OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
        }

        private async Task<List<Place>> FetchPartAsync(CategoryConfig category, Region region, string cacheFile,
            bool fromCache, TimeSpan timeout)
        {
            var cachePath = Path.Combine(Path.GetTempPath(), cacheFile);
            if (!fromCache || !File.Exists(cachePath))
                await QueryAsync(BuildQuery(category, region), cachePath, timeout);
            else
                Console.WriteLine($"[{category.Id}] Verwende Cache {cachePath}");

            await using var stream = File.OpenRead(cachePath);
            using var json = await JsonDocument.ParseAsync(stream);
            return ParsePlaces(json.RootElement, category, region);
        }

        /// <summary>
        /// Iteriert über die Länderflächen. Overpass gibt pro Land zuerst die Fläche (mit ISO-Code) und danach
        /// deren Orte aus – so lässt sich jedem Ort ohne Geocoding ein Land zuordnen.
        /// </summary>
        private static string BuildQuery(CategoryConfig category, Region region) => $$"""
            [out:json][timeout:900];
            area["ISO3166-1"~"^({{string.Join('|', region.Countries)}})$"]["admin_level"="2"]->.countries;
            foreach.countries->.country(
              .country out tags;
              {{category.Filter}}(area.country);
              out center tags;
            );
            """;

        private const int AttemptsPerEndpoint = 2;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Overpass ist oft kurzzeitig überlastet (504/429). Deshalb pro Endpunkt mehrere Versuche mit Pause,
        /// bevor auf den nächsten Endpunkt ausgewichen wird.
        /// </summary>
        // Endpunkte, die in diesem Lauf wiederholt versagt haben, werden übersprungen (spart bei vielen
        // Einzelabfragen pro Land die Wartezeit für einen dauerhaft gestörten Server)
        private readonly HashSet<string> _skipped = [];

        private async Task QueryAsync(string query, string targetPath, TimeSpan timeout)
        {
            var endpoints = Endpoints.Where(e => !_skipped.Contains(e)).ToList();
            if (endpoints.Count == 0)
            {
                _skipped.Clear(); // alle gestört – dann doch wieder alle probieren
                endpoints = [.. Endpoints];
            }

            foreach (var endpoint in endpoints)
            {
                for (var attempt = 1; attempt <= AttemptsPerEndpoint; attempt++)
                {
                    try
                    {
                        using var cts = new CancellationTokenSource(timeout);
                        using var response = await http.PostAsync(endpoint,
                            new FormUrlEncodedContent([new("data", query)]), cts.Token);
                        response.EnsureSuccessStatusCode();
                        var body = await response.Content.ReadAsStringAsync(cts.Token);
                        EnsureComplete(body);
                        await File.WriteAllTextAsync(targetPath, body, cts.Token);
                        _skipped.Remove(endpoint);
                        return;
                    }
                    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
                    {
                        Console.Error.WriteLine($"  {endpoint} Versuch {attempt}/{AttemptsPerEndpoint} fehlgeschlagen: {ex.Message}");
                        if (attempt < AttemptsPerEndpoint) await Task.Delay(RetryDelay);
                    }
                }
                if (_skipped.Add(endpoint))
                    Console.Error.WriteLine($"  {endpoint} wird für den Rest des Laufs übersprungen");
            }
            throw new InvalidOperationException("Alle Overpass-Endpunkte sind fehlgeschlagen.");
        }

        /// <summary>
        /// Bei Zeitüberschreitung auf dem Server antwortet Overpass trotzdem mit 200 und unvollständigen Daten –
        /// erkennbar nur an "remark". Solche Antworten nicht speichern.
        /// </summary>
        private static void EnsureComplete(string body)
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("remark", out var remark)
                && remark.GetString() is { } text && text.Contains("error", StringComparison.OrdinalIgnoreCase))
                throw new HttpRequestException($"Unvollständige Antwort: {text}");
        }

        // JsonDocument statt JsonNode: OSM-Tags können doppelte Schlüssel enthalten (z. B. "flag"),
        // daran scheitert JsonNode, JsonDocument nimmt einfach den ersten Treffer.
        private static List<Place> ParsePlaces(JsonElement json, CategoryConfig category, Region region)
        {
            var places = new Dictionary<string, Place>();
            string? country = null;

            foreach (var element in json.GetProperty("elements").EnumerateArray())
            {
                var type = element.GetProperty("type").GetString()!;
                element.TryGetProperty("tags", out var tags);
                if (type == "area")
                {
                    country = Tag(tags, "ISO3166-1");
                    continue;
                }
                if (country is null) continue;

                var position = type == "node" ? element : element.GetProperty("center");
                var lat = position.GetProperty("lat").GetDouble();
                var lon = position.GetProperty("lon").GetDouble();
                // Überseegebiete (z. B. Réunion) gehören zur Länderfläche, aber nicht auf die Europakarte
                if (!region.Bbox.Contains(lat, lon)) continue;

                var id = type[0] + element.GetProperty("id").GetInt64().ToString(CultureInfo.InvariantCulture);
                var name = Tag(tags, "name");
                var street = Tag(tags, "addr:street");
                var house = Tag(tags, "addr:housenumber");

                // An Grenzen kann ein Ort in zwei Länderflächen liegen – der erste Treffer gewinnt
                places.TryAdd(id, new Place(
                    id,
                    name == category.Name ? null : name,
                    Math.Round(lat, 5),
                    Math.Round(lon, 5),
                    street is null ? null : house is null ? street : $"{street} {house}",
                    Tag(tags, "addr:city") ?? Tag(tags, "addr:place"),
                    Tag(tags, "addr:postcode"),
                    country));
            }

            return places.Values.OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
        }

        private static string? Tag(JsonElement tags, string key) =>
            tags.ValueKind == JsonValueKind.Object && tags.TryGetProperty(key, out var value)
            && value.GetString() is { Length: > 0 } text
                ? text.Trim()
                : null;
    }

    static class PlaceMerger
    {
        private const double MaxDistanceKm = 1.5;

        /// <summary>
        /// Fasst Einträge mit gleichem Namen im selben Land zusammen, die näher als 1,5 km beieinander liegen.
        /// Bevorzugt bleibt die größte Einheit (Relation vor Fläche vor Punkt – also eher der Campus als ein
        /// einzelnes Gebäude) und ein Eintrag mit Adresse. Weit entfernte Standorte (z. B. zweiter Campus) bleiben.
        /// </summary>
        public static List<Place> MergeSameName(List<Place> places)
        {
            var result = new List<Place>();
            foreach (var group in places.GroupBy(p => (p.Cc, Name: p.N?.Trim().ToLowerInvariant())))
            {
                if (string.IsNullOrEmpty(group.Key.Name))
                {
                    result.AddRange(group);
                    continue;
                }

                var kept = new List<Place>();
                foreach (var place in group
                             .OrderBy(p => p.Id[0] switch { 'r' => 0, 'w' => 1, _ => 2 })
                             .ThenBy(p => p.A is null)
                             .ThenBy(p => p.Id, StringComparer.Ordinal))
                {
                    if (!kept.Any(k => DistanceKm(k, place) < MaxDistanceKm))
                        kept.Add(place);
                }
                result.AddRange(kept);
            }
            return result.OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
        }

        private static double DistanceKm(Place a, Place b)
        {
            static double Rad(double deg) => deg * Math.PI / 180;
            var dLat = Rad(b.Lat - a.Lat);
            var dLon = Rad(b.Lon - a.Lon);
            var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                    + Math.Cos(Rad(a.Lat)) * Math.Cos(Rad(b.Lat)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return 2 * 6371.0 * Math.Asin(Math.Sqrt(h));
        }
    }

    static class PlacesWriter
    {
        /// <summary>Ein Ort pro Zeile: kompakt, aber mit lesbaren Git-Diffs bei Daten-Updates.</summary>
        public static string Write(CategoryConfig category, List<Place> places, DateTimeOffset generatedAt)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append($"  \"category\": {JsonSerializer.Serialize(category.Id)},\n");
            sb.Append("  \"source\": \"© OpenStreetMap contributors, ODbL\",\n");
            sb.Append($"  \"generatedAt\": \"{generatedAt.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}\",\n");
            sb.Append("  \"places\": [\n");
            for (var i = 0; i < places.Count; i++)
            {
                sb.Append("    ").Append(JsonSerializer.Serialize(places[i], Json.WriteCompact));
                sb.Append(i < places.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("  ]\n}\n");
            return sb.ToString();
        }
    }
}
