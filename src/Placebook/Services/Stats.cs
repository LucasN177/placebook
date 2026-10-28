using Placebook.Models;

namespace Placebook.Services;

public sealed record CountryStats(string CountryCode, int Total, int Visited)
{
    public double Percent => Total == 0 ? 0 : 100.0 * Visited / Total;
}

public sealed record MonthStats(DateOnly Month, int Count);

/// <summary>Kennzahlen für die Statistik-Seite.</summary>
public static class Stats
{
    /// <summary>Pro Land: Anzahl Orte und davon besucht – besuchte Länder zuerst, dann nach Größe.</summary>
    public static List<CountryStats> ByCountry(IEnumerable<Place> places, Func<string, bool> isVisited) =>
        places
            .GroupBy(p => p.CountryCode)
            .Select(g => new CountryStats(g.Key, g.Count(), g.Count(p => isVisited(p.Id))))
            .OrderByDescending(c => c.Visited)
            .ThenByDescending(c => c.Total)
            .ToList();

    /// <summary>Besuche pro Monat für die letzten <paramref name="months"/> Monate bis einschließlich <paramref name="today"/>.</summary>
    public static List<MonthStats> ByMonth(IEnumerable<Visit> visits, DateOnly today, int months = 12)
    {
        var counts = visits
            .Where(v => v.VisitedAt is not null)
            .GroupBy(v => FirstOfMonth(v.VisitedAt!.Value))
            .ToDictionary(g => g.Key, g => g.Count());

        var start = FirstOfMonth(today).AddMonths(-(months - 1));
        return Enumerable.Range(0, months)
            .Select(i => start.AddMonths(i))
            .Select(month => new MonthStats(month, counts.GetValueOrDefault(month)))
            .ToList();
    }

    private static DateOnly FirstOfMonth(DateOnly date) => new(date.Year, date.Month, 1);
}
