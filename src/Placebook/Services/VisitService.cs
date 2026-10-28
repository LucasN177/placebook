using System.Text.Json;
using Microsoft.JSInterop;
using Placebook.Models;

namespace Placebook.Services;

/// <summary>Verwaltet, welche Orte besucht wurden, und speichert das im localStorage des Browsers.</summary>
public sealed class VisitService(IJSRuntime js)
{
    public const string StorageKey = "placebook.visits.v1";

    private VisitStore _store = new();
    private bool _loaded;

    /// <summary>Wird nach jeder Änderung ausgelöst.</summary>
    public event Action? Changed;

    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        var json = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
        _store = Deserialize(json) ?? new VisitStore();
        _loaded = true;
    }

    public bool IsVisited(string categoryId, string placeId) => GetVisit(categoryId, placeId) is not null;

    public Visit? GetVisit(string categoryId, string placeId) =>
        _store.Visits.TryGetValue(categoryId, out var visits) && visits.TryGetValue(placeId, out var visit) ? visit : null;

    public IReadOnlyCollection<string> GetVisitedIds(string categoryId) =>
        _store.Visits.TryGetValue(categoryId, out var visits) ? visits.Keys : [];

    public IReadOnlyDictionary<string, Visit> GetVisits(string categoryId) =>
        _store.Visits.TryGetValue(categoryId, out var visits) ? visits : new Dictionary<string, Visit>();

    public async Task SetVisitAsync(string categoryId, string placeId, Visit visit)
    {
        if (!_store.Visits.TryGetValue(categoryId, out var visits))
            _store.Visits[categoryId] = visits = new();
        visits[placeId] = visit;
        await SaveAsync();
    }

    public async Task RemoveVisitAsync(string categoryId, string placeId)
    {
        if (_store.Visits.TryGetValue(categoryId, out var visits) && visits.Remove(placeId))
            await SaveAsync();
    }

    public int CountVisited(string categoryId) =>
        _store.Visits.TryGetValue(categoryId, out var visits) ? visits.Count : 0;

    public string Export() => Serialize(_store);

    /// <summary>
    /// Übernimmt einen Export. Beim Zusammenführen bleiben vorhandene Einträge erhalten und es kommen nur neue
    /// Orte hinzu; sonst wird der aktuelle Stand vollständig ersetzt.
    /// </summary>
    /// <returns>Anzahl der Besuche in der importierten Datei.</returns>
    /// <exception cref="FormatException">Die Datei ist kein gültiger Placebook-Export.</exception>
    public async Task<int> ImportAsync(string json, bool merge)
    {
        var imported = Deserialize(json) ?? throw new FormatException("Die Datei ist kein gültiger Placebook-Export.");
        _store = merge ? Merge(_store, imported) : imported;
        await SaveAsync();
        return imported.Visits.Sum(c => c.Value.Count);
    }

    public static VisitStore Merge(VisitStore current, VisitStore imported)
    {
        var result = new VisitStore();
        foreach (var (categoryId, visits) in current.Visits)
            result.Visits[categoryId] = new(visits);

        foreach (var (categoryId, visits) in imported.Visits)
        {
            if (!result.Visits.TryGetValue(categoryId, out var target))
                result.Visits[categoryId] = target = new();
            foreach (var (placeId, visit) in visits)
                target.TryAdd(placeId, visit);
        }
        return result;
    }

    private async Task SaveAsync()
    {
        await js.InvokeVoidAsync("localStorage.setItem", StorageKey, Serialize(_store));
        Changed?.Invoke();
    }

    public static string Serialize(VisitStore store) => JsonSerializer.Serialize(store, PlacebookJsonContext.Default.VisitStore);

    public static VisitStore? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var store = JsonSerializer.Deserialize(json, PlacebookJsonContext.Default.VisitStore);
            return store?.Version == VisitStore.CurrentVersion ? store : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
