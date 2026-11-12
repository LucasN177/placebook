using System.Text.Json.Serialization;

namespace Placebook.Models;

/// <summary>Besuch eines Ortes. Es gibt pro Ort höchstens einen Eintrag (keine Mehrfachbesuche).</summary>
/// <param name="UpdatedAt">Zeitpunkt der letzten Änderung; entscheidet beim Synchronisieren, welcher Stand gewinnt.
/// Fehlt bei Besuchen aus älteren Versionen.</param>
/// <param name="MulledWine">Nur bei Kategorien mit <see cref="Category.MulledWine"/>: true = Glühwein getrunken,
/// null = keine Angabe.</param>
public sealed record Visit(
    DateOnly? VisitedAt,
    string? Note,
    DateTimeOffset? UpdatedAt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? MulledWine = null)
{
    /// <summary>Gleicher Inhalt, unabhängig vom Änderungszeitpunkt.</summary>
    public bool HasSameContent(Visit other) =>
        VisitedAt == other.VisitedAt && Note == other.Note && MulledWine == other.MulledWine;
}

/// <summary>Eine Änderung an einem Besuch, wie sie synchronisiert wird. <see cref="Visit"/> ist null, wenn der Besuch gelöscht wurde.</summary>
public sealed record VisitChange(string CategoryId, string PlaceId, Visit? Visit, DateTimeOffset UpdatedAt);

/// <summary>
/// Gespeicherter Besucht-Status (localStorage-Key <c>placebook.visits.v1</c>).
/// Struktur: Kategorie-ID → Orts-ID → Besuch.
/// </summary>
public sealed class VisitStore
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public Dictionary<string, Dictionary<string, Visit>> Visits { get; set; } = new();
}
