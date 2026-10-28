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
