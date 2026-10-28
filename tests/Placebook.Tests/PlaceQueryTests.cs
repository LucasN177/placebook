using System.Globalization;
using Placebook.Models;
using Placebook.Services;

namespace Placebook.Tests;

public class PlaceQueryTests
{
    private static readonly Place Berlin = new("n1", null, 52.52, 13.405, "Alexanderplatz 1", "Berlin", "10178", "DE");
    private static readonly Place Wien = new("n2", null, 48.2085, 16.3731, "Singerstraße 4", "Wien", "1010", "AT");
    private static readonly Place Paris = new("n3", "McDonald's Opéra", 48.871, 2.332, null, "Paris", "75009", "FR");
    private static readonly Place NoCity = new("n4", null, 50.0, 8.0, null, null, null, "DE");
    private static readonly Place[] All = [Wien, NoCity, Paris, Berlin];

    public PlaceQueryTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("de-DE");
    }

    [Fact]
    public void DefaultSort_IsByCity_WithUnknownCityLast()
    {
        var ids = Run().Select(i => i.Place.Id);

        Assert.Equal(["n1", "n3", "n2", "n4"], ids);
    }

    [Theory]
    [InlineData("wien", "n2")]
    [InlineData("1010", "n2")]
    [InlineData("Österreich", "n2")]
    [InlineData("opéra", "n3")]
    [InlineData("alexanderplatz berlin", "n1")]
    public void Search_MatchesAllTermsAcrossFields(string search, string expectedId)
    {
        var result = Run(search: search);

        Assert.Equal(expectedId, Assert.Single(result).Place.Id);
    }

    [Fact]
    public void Filter_SeparatesVisitedAndOpen()
    {
        Assert.Equal(["n2"], Run(filter: VisitFilter.Visited).Select(i => i.Place.Id));
        Assert.DoesNotContain(Run(filter: VisitFilter.Open), i => i.Place.Id == "n2");
    }

    [Fact]
    public void DistanceSort_StartsWithNearestPlace()
    {
        var nearStephansplatz = new GeoPosition(48.2085, 16.3731, 10);

        var result = Run(sort: PlaceSort.Distance, position: nearStephansplatz);

        Assert.Equal("n2", result[0].Place.Id);
        Assert.True(result.Select(i => i.DistanceKm!.Value).SequenceEqual(result.Select(i => i.DistanceKm!.Value).Order()));
    }

    [Fact]
    public void DistanceSort_WithoutPosition_FallsBackToCity()
    {
        Assert.Equal(Run().Select(i => i.Place.Id), Run(sort: PlaceSort.Distance).Select(i => i.Place.Id));
    }

    private static List<PlaceListItem> Run(string? search = null, VisitFilter filter = VisitFilter.All,
        PlaceSort sort = PlaceSort.City, GeoPosition? position = null) =>
        PlaceQuery.Run(All, id => id == "n2", search, filter, sort, position);
}
