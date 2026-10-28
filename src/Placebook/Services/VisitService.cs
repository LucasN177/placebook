using System.Text.Json;
using Microsoft.JSInterop;
using Placebook.Models;

namespace Placebook.Services;

/// <summary>Verwaltet, welche Orte besucht wurden, und speichert das im localStorage des Browsers.</summary>
public sealed class VisitService(IJSRuntime js, TimeProvider time)
{
    public const string StorageKey = "placebook.visits.v1";

    private VisitStore _store = new();
    private bool _loaded;

    /// <summary>Wird nach jeder Änderung ausgelöst.</summary>
    public event Action? Changed;

    /// <summary>
    /// Wird ausgelöst, wenn sich viele Besuche auf einmal von außen geändert haben (Import, Synchronisierung) –
    /// z. B. muss die Karte dann alle Marker neu zeichnen.
    /// </summary>
    public event Action? ExternallyChanged;

    /// <summary>Änderungen durch den Nutzer auf diesem Gerät, die synchronisiert werden sollen.</summary>
    public event Action<IReadOnlyList<VisitChange>>? LocalChanged;

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

    public int CountVisited(string categoryId) =>
        _store.Visits.TryGetValue(categoryId, out var visits) ? visits.Count : 0;

    /// <summary>Alle Besuche als Änderungen – für den ersten Abgleich mit dem Server.</summary>
    public IReadOnlyList<VisitChange> AllAsChanges() =>
        _store.Visits
            .SelectMany(c => c.Value.Select(v =>
                new VisitChange(c.Key, v.Key, v.Value, v.Value.UpdatedAt ?? DateTimeOffset.UnixEpoch)))
            .ToList();

    public async Task SetVisitAsync(string categoryId, string placeId, Visit visit)
    {
        var now = time.GetUtcNow();
        visit = visit with { UpdatedAt = now };
        Put(categoryId, placeId, visit);
        await SaveAsync();
        LocalChanged?.Invoke([new VisitChange(categoryId, placeId, visit, now)]);
    }

    public async Task RemoveVisitAsync(string categoryId, string placeId)
    {
        if (!_store.Visits.TryGetValue(categoryId, out var visits) || !visits.Remove(placeId)) return;
        await SaveAsync();
        LocalChanged?.Invoke([new VisitChange(categoryId, placeId, null, time.GetUtcNow())]);
    }

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
        var now = time.GetUtcNow();
        var before = AllAsChanges().ToDictionary(c => (c.CategoryId, c.PlaceId), c => c.Visit!);

        _store = merge ? Merge(_store, imported) : imported;

        // Was der Import tatsächlich geändert hat, gilt als Änderung von jetzt – so gewinnt es beim Synchronisieren
        var changes = new List<VisitChange>();
        foreach (var (categoryId, visits) in _store.Visits)
        {
            foreach (var (placeId, visit) in visits.ToList())
            {
                if (before.TryGetValue((categoryId, placeId), out var old) && SameContent(old, visit)) continue;
                visits[placeId] = visit with { UpdatedAt = now };
                changes.Add(new VisitChange(categoryId, placeId, visits[placeId], now));
            }
        }
        changes.AddRange(before.Keys
            .Where(key => GetVisit(key.CategoryId, key.PlaceId) is null)
            .Select(key => new VisitChange(key.CategoryId, key.PlaceId, null, now)));

        await SaveAsync();
        ExternallyChanged?.Invoke();
        LocalChanged?.Invoke(changes);
        return imported.Visits.Sum(c => c.Value.Count);
    }

    /// <summary>Übernimmt Änderungen von anderen Geräten (bereits gefiltert, siehe <see cref="SyncMerge"/>).</summary>
    public async Task ApplyRemoteAsync(IReadOnlyList<VisitChange> changes)
    {
        if (changes.Count == 0) return;
        foreach (var change in changes)
        {
            if (change.Visit is null)
            {
                if (_store.Visits.TryGetValue(change.CategoryId, out var visits))
                    visits.Remove(change.PlaceId);
            }
            else
            {
                Put(change.CategoryId, change.PlaceId, change.Visit with { UpdatedAt = change.UpdatedAt });
            }
        }
        await SaveAsync();
        ExternallyChanged?.Invoke();
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

    private static bool SameContent(Visit a, Visit b) => a.VisitedAt == b.VisitedAt && a.Note == b.Note;

    private void Put(string categoryId, string placeId, Visit visit)
    {
        if (!_store.Visits.TryGetValue(categoryId, out var visits))
            _store.Visits[categoryId] = visits = new();
        visits[placeId] = visit;
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
