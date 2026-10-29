using Placebook.Models;

namespace Placebook.Services;

public enum VisitFilter { All, Visited, Open }

public enum PlaceSort { City, Country, Distance }

public sealed record PlaceListItem(Place Place, bool IsVisited, double? DistanceKm);

/// <summary>Suche, Filter und Sortierung für die Listenansicht.</summary>
public static class PlaceQuery
{
    /// <param name="search">Leerzeichen-getrennte Begriffe; jeder muss in Name, Adresse, PLZ, Ort oder Land vorkommen.</param>
    /// <param name="position">Eigener Standort für Entfernungen; ohne Standort wird nach Entfernung nicht sortiert.</param>
    /// <param name="countryCode">Nur Orte in diesem Land (Länderansicht); null für alle.</param>
    public static List<PlaceListItem> Run(
        IEnumerable<Place> places,
        Func<string, bool> isVisited,
        string? search,
        VisitFilter filter,
        PlaceSort sort,
        GeoPosition? position,
        string? countryCode = null)
    {
        var terms = (search ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var items = places
            .Where(p => countryCode is null || p.CountryCode == countryCode)
            .Select(p => new PlaceListItem(p, isVisited(p.Id),
                position is null ? null : Geo.DistanceKm(position.Lat, position.Lon, p.Lat, p.Lon)))
            .Where(i => filter switch
            {
                VisitFilter.Visited => i.IsVisited,
                VisitFilter.Open => !i.IsVisited,
                _ => true
            })
            .Where(i => terms.All(term => Matches(i.Place, term)));

        return (sort switch
        {
            PlaceSort.Distance when position is not null => items.OrderBy(i => i.DistanceKm),
            PlaceSort.Country => items
                .OrderBy(i => Countries.Name(i.Place.CountryCode), StringComparer.CurrentCulture)
                .ThenBy(i => i.Place.City, StringComparer.CurrentCulture),
            _ => items
                .OrderBy(i => i.Place.City is null) // Orte ohne Stadt ans Ende
                .ThenBy(i => i.Place.City, StringComparer.CurrentCulture)
                .ThenBy(i => i.Place.Address, StringComparer.CurrentCulture)
        }).ToList();
    }

    private static bool Matches(Place place, string term) =>
        Contains(place.Name, term)
        || Contains(place.Address, term)
        || Contains(place.City, term)
        || Contains(place.PostalCode, term)
        || Contains(place.CountryCode, term)
        || Contains(Countries.Name(place.CountryCode), term);

    private static bool Contains(string? value, string term) =>
        value is not null && value.Contains(term, StringComparison.CurrentCultureIgnoreCase);
}
