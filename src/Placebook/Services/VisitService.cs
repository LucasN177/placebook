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
