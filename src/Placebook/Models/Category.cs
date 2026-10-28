namespace Placebook.Models;

/// <summary>Eintrag aus <c>data/categories.json</c>.</summary>
/// <param name="ItemName">Bezeichnung eines einzelnen Orts ohne eigenen Namen (z. B. „Krankenhaus“); sonst gilt <see cref="Name"/>.</param>
/// <param name="MissingCountries">Nur bei Teildaten: Länder, für die noch keine Orte abgerufen werden konnten.</param>
public sealed record Category(
    string Id,
    string Name,
    string Icon,
    string Color,
    string File,
    int Count,
    DateOnly UpdatedAt,
    IReadOnlyList<string>? MissingCountries = null,
    string? ItemName = null)
{
    public bool IsPartial => MissingCountries is { Count: > 0 };
}
