using Placebook.Services;

namespace Placebook.Tests;

public class GeoTests
{
    [Fact]
    public void DistanceKm_BerlinToMunich_IsAbout504Km()
    {
        var km = Geo.DistanceKm(52.5200, 13.4050, 48.1351, 11.5820);

        Assert.InRange(km, 500, 508);
    }

    [Fact]
    public void DistanceKm_SamePoint_IsZero()
    {
        Assert.Equal(0, Geo.DistanceKm(48.2, 16.37, 48.2, 16.37), 6);
    }

    [Theory]
    [InlineData(0.25, "250 m")]
    [InlineData(3.456, "3,5 km")]
    [InlineData(1234.4, "1.234 km")]
    public void FormatDistance_PicksSuitableUnit(double km, string expected)
    {
        var culture = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        try
        {
            Assert.Equal(expected, Geo.FormatDistance(km));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = culture;
        }
    }
}
