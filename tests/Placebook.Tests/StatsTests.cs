using Placebook.Models;
using Placebook.Services;

namespace Placebook.Tests;

public class StatsTests
{
    [Fact]
    public void ByCountry_CountsAndOrdersVisitedCountriesFirst()
    {
        Place[] places =
        [
            new("n1", null, 0, 0, null, null, null, "DE"),
            new("n2", null, 0, 0, null, null, null, "DE"),
            new("n3", null, 0, 0, null, null, null, "DE"),
            new("n4", null, 0, 0, null, null, null, "AT"),
            new("n5", null, 0, 0, null, null, null, "AT")
        ];

        var stats = Stats.ByCountry(places, id => id == "n4");

        Assert.Equal(new CountryStats("AT", 2, 1), stats[0]);
        Assert.Equal(new CountryStats("DE", 3, 0), stats[1]);
        Assert.Equal(50, stats[0].Percent);
    }

    [Fact]
    public void ByMonth_ReturnsLast12MonthsIncludingEmptyOnes()
    {
        Visit[] visits =
        [
            new(new DateOnly(2026, 10, 3), null),
            new(new DateOnly(2026, 10, 20), null),
            new(new DateOnly(2026, 1, 15), null),
            new(new DateOnly(2025, 6, 1), null), // älter als 12 Monate
            new(null, null)                       // ohne Datum
        ];

        var months = Stats.ByMonth(visits, new DateOnly(2026, 10, 28));

        Assert.Equal(12, months.Count);
        Assert.Equal(new DateOnly(2025, 11, 1), months[0].Month);
        Assert.Equal(new MonthStats(new DateOnly(2026, 10, 1), 2), months[^1]);
        Assert.Equal(1, months.Single(m => m.Month == new DateOnly(2026, 1, 1)).Count);
        Assert.Equal(3, months.Sum(m => m.Count));
    }
}
