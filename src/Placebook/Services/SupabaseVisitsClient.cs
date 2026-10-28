using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Placebook.Models;

namespace Placebook.Services;

/// <summary>Zugangsdaten aus <c>wwwroot/appsettings.json</c> (Abschnitt "Supabase"). Der Publishable Key ist öffentlich.</summary>
public sealed class SupabaseOptions
{
    public string? Url { get; set; }
    public string? PublishableKey { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(PublishableKey);
}

/// <summary>Zeile der Tabelle <c>visits</c> beim Hochladen (siehe supabase/migrations/001_visits.sql).</summary>
public sealed record VisitPushRow(
    [property: JsonPropertyName("sync_key")] Guid SyncKey,
    [property: JsonPropertyName("category_id")] string CategoryId,
    [property: JsonPropertyName("place_id")] string PlaceId,
    [property: JsonPropertyName("visited_at")] DateOnly? VisitedAt,
    [property: JsonPropertyName("note")] string? Note,
    [property: JsonPropertyName("deleted")] bool Deleted,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

/// <summary>Zeile der Tabelle <c>visits</c> beim Abholen.</summary>
public sealed record VisitPullRow(
    [property: JsonPropertyName("category_id")] string CategoryId,
    [property: JsonPropertyName("place_id")] string PlaceId,
    [property: JsonPropertyName("visited_at")] DateOnly? VisitedAt,
    [property: JsonPropertyName("note")] string? Note,
    [property: JsonPropertyName("deleted")] bool Deleted,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("synced_at")] DateTimeOffset SyncedAt)
{
    public VisitChange ToChange() =>
        new(CategoryId, PlaceId, Deleted ? null : new Visit(VisitedAt, Note, UpdatedAt), UpdatedAt);
}

/// <summary>
/// Minimaler Client für die REST-Schnittstelle von Supabase (PostgREST). Der Sync-Code geht im Header
/// <c>x-sync-key</c> mit; die Row-Level-Security-Regel gibt nur dessen Zeilen frei.
/// </summary>
public sealed class SupabaseVisitsClient(HttpClient http, SupabaseOptions options)
{
    public const int PageSize = 1000;

    private const string Columns = "category_id,place_id,visited_at,note,deleted,updated_at,synced_at";

    public bool IsConfigured => options.IsConfigured;

    /// <summary>Lädt Änderungen hoch. Ältere Stände überschreiben auf dem Server keine neueren (Trigger).</summary>
    public async Task PushAsync(Guid syncKey, IReadOnlyList<VisitChange> changes, CancellationToken cancellationToken = default)
    {
        var rows = changes
            .Select(c => new VisitPushRow(syncKey, c.CategoryId, c.PlaceId, c.Visit?.VisitedAt, c.Visit?.Note,
                c.Visit is null, c.UpdatedAt))
            .ToList();

        using var request = CreateRequest(HttpMethod.Post, "visits?on_conflict=sync_key,category_id,place_id", syncKey);
        request.Headers.Add("Prefer", "resolution=merge-duplicates,return=minimal");
        request.Content = JsonContent.Create(rows, PlacebookJsonContext.Default.ListVisitPushRow);

        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response);
    }

    /// <summary>Holt eine Seite von Änderungen, die der Server ab <paramref name="since"/> gespeichert hat.</summary>
    public async Task<List<VisitPullRow>> PullAsync(Guid syncKey, DateTimeOffset? since, int offset,
        CancellationToken cancellationToken = default)
    {
        var query = $"visits?select={Columns}&order=synced_at.asc,category_id.asc,place_id.asc&limit={PageSize}&offset={offset}";
        if (since is { } value)
            query += "&synced_at=gte." + Uri.EscapeDataString(
                value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ", CultureInfo.InvariantCulture));

        using var request = CreateRequest(HttpMethod.Get, query, syncKey);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response);
        // Als String statt gestreamt einlesen – siehe CategoryService.LoadPlacesAsync
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize(json, PlacebookJsonContext.Default.ListVisitPullRow) ?? [];
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativeUrl, Guid syncKey)
    {
        var request = new HttpRequestMessage(method, $"{options.Url!.TrimEnd('/')}/rest/v1/{relativeUrl}");
        request.Headers.Add("apikey", options.PublishableKey);
        request.Headers.Add("x-sync-key", syncKey.ToString());
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync();
        throw new HttpRequestException($"Supabase antwortet mit {(int)response.StatusCode}: {body}", null, response.StatusCode);
    }
}
