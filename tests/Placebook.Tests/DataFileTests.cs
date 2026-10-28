using System.Text.Json;
using Placebook.Models;
using Placebook.Services;

namespace Placebook.Tests;

/// <summary>Prüft die committeten Ortsdateien unter wwwroot/data.</summary>
public class DataFileTests
{
    private static readonly string DataDir = Path.Combine(FindRepoRoot(), "src", "Placebook", "wwwroot", "data");

    public static TheoryData<string> CategoryIds() => new(LoadCategories().Select(c => c.Id));

    [Fact]
    public void CategoriesIndex_IsNotEmpty_AndIdsAreUnique()
    {
        var categories = LoadCategories();

        Assert.NotEmpty(categories);
        Assert.Equal(categories.Count, categories.Select(c => c.Id).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(CategoryIds))]
    public void PlacesFile_MatchesIndex(string categoryId)
    {
        var category = LoadCategories().Single(c => c.Id == categoryId);
        var file = LoadPlaces(category);

        Assert.Equal(category.Id, file.Category);
        Assert.Equal(category.Count, file.Places.Count);
    }

    [Theory]
    [MemberData(nameof(CategoryIds))]
    public void Places_HaveUniqueIds_AndLieInEurope(string categoryId)
    {
        var places = LoadPlaces(LoadCategories().Single(c => c.Id == categoryId)).Places;

        Assert.Equal(places.Count, places.Select(p => p.Id).Distinct().Count());
        Assert.All(places, p =>
        {
            Assert.Matches("^[nwr][0-9]+$", p.Id);
            Assert.InRange(p.Lat, 27.0, 72.0);
            Assert.InRange(p.Lon, -32.0, 45.0);
            Assert.Matches("^[A-Z]{2}$", p.CountryCode);
        });
    }

    private static List<Category> LoadCategories() =>
        JsonSerializer.Deserialize<List<Category>>(
            File.ReadAllText(Path.Combine(DataDir, "categories.json")), CategoryService.JsonOptions)!;

    private static PlacesFile LoadPlaces(Category category) =>
        JsonSerializer.Deserialize<PlacesFile>(
            File.ReadAllText(Path.Combine(DataDir, category.File)), CategoryService.JsonOptions)!;

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Placebook.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Placebook.sln nicht gefunden.");
    }
}
