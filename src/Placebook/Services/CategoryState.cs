using Microsoft.JSInterop;
using Placebook.Models;

namespace Placebook.Services;

/// <summary>
/// Die aktuell gewählte Kategorie samt ihrer Orte – geteilt zwischen Karte, Liste und Statistik.
/// Die Auswahl wird im localStorage gemerkt.
/// </summary>
public sealed class CategoryState(CategoryService categories, IJSRuntime js)
{
    private const string StorageKey = "placebook.category";

    private Task? _loading;

    public IReadOnlyList<Category> Categories { get; private set; } = [];
    public Category? Current { get; private set; }
    public IReadOnlyList<Place> Places { get; private set; } = [];
    public IReadOnlyDictionary<string, Place> PlacesById { get; private set; } = new Dictionary<string, Place>();

    /// <summary>Wird ausgelöst, nachdem eine andere Kategorie gewählt und ihre Orte geladen wurden.</summary>
    public event Action? Changed;

    /// <summary>Lädt beim ersten Aufruf Kategorien und Orte; weitere Aufrufe warten auf denselben Ladevorgang.</summary>
    public Task EnsureLoadedAsync() => _loading ??= LoadAsync();

    public async Task SelectAsync(Category category)
    {
        if (category.Id == Current?.Id) return;
        await ActivateAsync(category);
        await js.InvokeVoidAsync("localStorage.setItem", StorageKey, category.Id);
        Changed?.Invoke();
    }

    private async Task LoadAsync()
    {
        Categories = await categories.GetCategoriesAsync();
        if (Categories.Count == 0) return;

        var storedId = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
        await ActivateAsync(Categories.FirstOrDefault(c => c.Id == storedId) ?? Categories[0]);
        Changed?.Invoke();
    }

    private async Task ActivateAsync(Category category)
    {
        var places = await categories.GetPlacesAsync(category);
        Current = category;
        Places = places;
        PlacesById = places.ToDictionary(p => p.Id);
    }
}
