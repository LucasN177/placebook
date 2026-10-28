using Placebook.Models;

namespace Placebook.Tests;

public class PlaceTests
{
    private static readonly Category McDonalds =
        new("mcdonalds", "McDonald's", "fastfood", "#DA291C", "places/mcdonalds.json", 1, new DateOnly(2026, 10, 28));

    [Fact]
    public void DisplayName_FallsBackToCategoryName()
    {
        Assert.Equal("McDonald's", Place("n1").DisplayName(McDonalds));
        Assert.Equal("McDonald's Hbf", (Place("n1") with { Name = "McDonald's Hbf" }).DisplayName(McDonalds));
    }

    [Theory]
    [InlineData("Alexanderplatz 1", "Berlin", "10178", "Alexanderplatz 1, 10178 Berlin")]
    [InlineData(null, "Berlin", null, "Berlin")]
    [InlineData("Alexanderplatz 1", null, null, "Alexanderplatz 1")]
    [InlineData(null, null, null, null)]
    public void DisplayAddress_JoinsAvailableParts(string? address, string? city, string? postalCode, string? expected)
    {
        var place = Place("n1") with { Address = address, City = city, PostalCode = postalCode };

        Assert.Equal(expected, place.DisplayAddress);
    }

    private static Place Place(string id) => new(id, null, 52.52, 13.405, null, null, null, "DE");
}
