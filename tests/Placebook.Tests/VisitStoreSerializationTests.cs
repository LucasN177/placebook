using Placebook.Models;
using Placebook.Services;

namespace Placebook.Tests;

public class VisitStoreSerializationTests
{
    [Fact]
    public void RoundTrip_KeepsVisits()
    {
        var store = new VisitStore();
        store.Visits["mcdonalds"] = new() { ["n1"] = new Visit(new DateOnly(2026, 10, 20), "Bester McFlurry") };

        var restored = VisitService.Deserialize(VisitService.Serialize(store));

        Assert.NotNull(restored);
        Assert.Equal(new Visit(new DateOnly(2026, 10, 20), "Bester McFlurry"), restored.Visits["mcdonalds"]["n1"]);
    }

    [Fact]
    public void Merge_KeepsExistingEntries_AndAddsNewOnes()
    {
        var current = new VisitStore();
        current.Visits["mcdonalds"] = new() { ["n1"] = new Visit(new DateOnly(2026, 1, 1), "lokal") };
        var imported = new VisitStore();
        imported.Visits["mcdonalds"] = new()
        {
            ["n1"] = new Visit(new DateOnly(2025, 5, 5), "aus Datei"),
            ["n2"] = new Visit(null, null)
        };
        imported.Visits["ikea"] = new() { ["w9"] = new Visit(null, null) };

        var merged = VisitService.Merge(current, imported);

        Assert.Equal("lokal", merged.Visits["mcdonalds"]["n1"].Note);
        Assert.True(merged.Visits["mcdonalds"].ContainsKey("n2"));
        Assert.True(merged.Visits["ikea"].ContainsKey("w9"));
        Assert.Single(current.Visits["mcdonalds"]); // Eingabe bleibt unverändert
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("kein json")]
    [InlineData("""{"version":99,"visits":{}}""")]
    public void Deserialize_ReturnsNull_ForMissingInvalidOrUnknownVersion(string? json)
    {
        Assert.Null(VisitService.Deserialize(json));
    }
}
