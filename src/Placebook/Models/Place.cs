using System.Text.Json.Serialization;

namespace Placebook.Models;

/// <summary>
/// Ein Ort im kompakten Dateiformat (siehe docs/IMPLEMENTATION_PLAN.md, Abschnitt 3.2).
/// <see cref="Id"/> ist die OSM-ID mit Typ-Präfix (z. B. <c>n123</c>) und bleibt über Daten-Updates stabil.
/// </summary>
public sealed record Place(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("n")] string? Name,
    [property: JsonPropertyName("lat")] double Lat,
    [property: JsonPropertyName("lon")] double Lon,
    [property: JsonPropertyName("a")] string? Address,
    [property: JsonPropertyName("c")] string? City,
    [property: JsonPropertyName("z")] string? PostalCode,
    [property: JsonPropertyName("cc")] string CountryCode)
{
    /// <summary>Der Name fehlt in der Datei, wenn er dem Kategorienamen entspricht.</summary>
    public string DisplayName(Category category) => Name ?? category.Name;

    public string? DisplayAddress
    {
        get
        {
            var cityLine = string.Join(' ', new[] { PostalCode, City }.Where(s => s is not null));
            var address = string.Join(", ", new[] { Address, cityLine }.Where(s => !string.IsNullOrEmpty(s)));
            return address.Length > 0 ? address : null;
        }
    }
}

/// <summary>Inhalt von <c>data/places/&lt;kategorie&gt;.json</c>.</summary>
public sealed record PlacesFile(
    string Category,
    string Source,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<Place> Places);
