namespace Placebook.Models;

/// <summary>Eintrag aus <c>data/categories.json</c>.</summary>
/// <param name="ItemName">Bezeichnung eines einzelnen Orts ohne eigenen Namen (z. B. „Krankenhaus“); sonst gilt <see cref="Name"/>.</param>
/// <param name="MissingCountries">Nur bei Teildaten: Länder, für die noch keine Orte abgerufen werden konnten.</param>
/// <param name="MulledWine">Bei Besuchen lässt sich zusätzlich angeben, ob Glühwein getrunken wurde (Weihnachtsmärkte).</param>
/// <param name="Theme">Besonderes Erscheinungsbild, solange die Kategorie gewählt ist (derzeit nur „christmas“).</param>
public sealed record Category(
    string Id,
    string Name,
    string Icon,
    string Color,
    string File,
    int Count,
    DateOnly UpdatedAt,
    IReadOnlyList<string>? MissingCountries = null,
    string? ItemName = null,
    bool MulledWine = false,
    string? Theme = null)
{
    public bool IsPartial => MissingCountries is { Count: > 0 };

    /// <summary>Weihnachtliches Erscheinungsbild (Lichterkette, Schnee, Farben).</summary>
    public bool IsChristmasTheme => Theme == "christmas";
}
