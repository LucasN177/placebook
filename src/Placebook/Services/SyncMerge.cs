using Placebook.Models;

namespace Placebook.Services;

/// <summary>Reine Entscheidungslogik für die Synchronisierung („last write wins“ pro Ort).</summary>
public static class SyncMerge
{
    /// <summary>Fügt neue lokale Änderungen in die Warteschlange ein; pro Ort bleibt nur die neueste.</summary>
    public static List<VisitChange> Enqueue(IEnumerable<VisitChange> pending, IEnumerable<VisitChange> changes)
    {
        var byKey = new Dictionary<(string, string), VisitChange>();
        foreach (var change in pending.Concat(changes))
        {
            var key = (change.CategoryId, change.PlaceId);
            if (!byKey.TryGetValue(key, out var existing) || change.UpdatedAt >= existing.UpdatedAt)
                byKey[key] = change;
        }
        return byKey.Values.ToList();
    }

    /// <summary>
    /// Welche Änderungen vom Server übernommen werden: nur solche, die neuer sind als der lokale Stand und als
    /// eine noch nicht hochgeladene lokale Änderung. Gelöschte Orte, die lokal gar nicht existieren, sind kein Update.
    /// </summary>
    public static List<VisitChange> SelectRemote(
        IEnumerable<VisitChange> remote,
        Func<string, string, Visit?> getLocal,
        IReadOnlyCollection<VisitChange> pending)
    {
        var pendingByKey = pending.ToDictionary(p => (p.CategoryId, p.PlaceId), p => p.UpdatedAt);
        var result = new List<VisitChange>();

        // Pro Ort nur den neuesten Server-Stand betrachten
        foreach (var change in remote.GroupBy(r => (r.CategoryId, r.PlaceId)).Select(g => g.MaxBy(r => r.UpdatedAt)!))
        {
            var local = getLocal(change.CategoryId, change.PlaceId);
            if (change.Visit is null && local is null) continue;
            if (local is not null && change.Visit is not null && SameContent(local, change.Visit)) continue;

            var localUpdatedAt = local?.UpdatedAt ?? DateTimeOffset.MinValue;
            var pendingUpdatedAt = pendingByKey.GetValueOrDefault((change.CategoryId, change.PlaceId), DateTimeOffset.MinValue);
            if (change.UpdatedAt > localUpdatedAt && change.UpdatedAt > pendingUpdatedAt)
                result.Add(change);
        }
        return result;
    }

    private static bool SameContent(Visit a, Visit b) => a.VisitedAt == b.VisitedAt && a.Note == b.Note;
}
