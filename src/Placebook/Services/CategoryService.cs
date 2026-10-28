using System.Net.Http.Json;
using System.Text.Json;
using Placebook.Models;

namespace Placebook.Services;

/// <summary>
/// Lädt den Kategorie-Index und die Ortsdateien aus <c>wwwroot/data</c>. Ortsdateien werden erst bei Bedarf
/// geladen und danach im Speicher gehalten.
/// </summary>
public sealed class CategoryService(HttpClient http)
{
    public static readonly JsonSerializerOptions JsonOptions = PlacebookJsonContext.Default.Options;

    private IReadOnlyList<Category>? _categories;
    private readonly Dictionary<string, Task<IReadOnlyList<Place>>> _places = new();

    public async Task<IReadOnlyList<Category>> GetCategoriesAsync() =>
        _categories ??= await http.GetFromJsonAsync("data/categories.json", PlacebookJsonContext.Default.ListCategory) ?? [];

    public async Task<IReadOnlyList<Place>> GetPlacesAsync(Category category)
    {
        if (!_places.TryGetValue(category.Id, out var task))
        {
            task = LoadPlacesAsync(category);
            _places[category.Id] = task;
        }
        return await task;
    }

    private async Task<IReadOnlyList<Place>> LoadPlacesAsync(Category category)
    {
        // updatedAt als Cache-Buster, damit nach einem Daten-Refresh nicht die alte Datei aus dem Browser-Cache kommt
        var file = await http.GetFromJsonAsync($"data/{category.File}?v={category.UpdatedAt:yyyyMMdd}",
            PlacebookJsonContext.Default.PlacesFile);
        return file?.Places ?? [];
    }
}
