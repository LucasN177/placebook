namespace Placebook.Models;

/// <summary>Besuch eines Ortes. Es gibt pro Ort höchstens einen Eintrag (keine Mehrfachbesuche).</summary>
public sealed record Visit(DateOnly? VisitedAt, string? Note);

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
