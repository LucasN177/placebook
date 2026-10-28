using System.Globalization;
using System.Net.Http.Headers;
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

/// <summary>Wohin synchronisiert wird: ohne Login über einen Sync-Code, angemeldet in das eigene Konto.</summary>
public abstract record SyncTarget
{
    /// <summary>Tabelle <c>visits</c>, Zugriff über den Header <c>x-sync-key</c>.</summary>
    public sealed record Code(Guid Key) : SyncTarget;

    /// <summary>Tabelle <c>account_visits</c>, Zugriff über das Access-Token des angemeldeten Nutzers.</summary>
    public sealed record Account(Guid UserId) : SyncTarget;

    /// <summary>Stabile Kennung, um einen Wechsel des Ziels zu erkennen.</summary>
    public string Id => this switch
    {
        Code code => $"code:{code.Key}",
        Account account => $"user:{account.UserId}",
        _ => throw new InvalidOperationException()
    };
}

/// <summary>Zeile beim Hochladen. Je nach Ziel ist entweder <see cref="SyncKey"/> oder <see cref="UserId"/> gesetzt.</summary>
public sealed record VisitPushRow(
    [property: JsonPropertyName("sync_key"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? SyncKey,
    [property: JsonPropertyName("user_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? UserId,
    [property: JsonPropertyName("category_id")] string CategoryId,
    [property: JsonPropertyName("place_id")] string PlaceId,
    [property: JsonPropertyName("visited_at")] DateOnly? VisitedAt,
    [property: JsonPropertyName("note")] string? Note,
    [property: JsonPropertyName("deleted")] bool Deleted,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

/// <summary>Zeile beim Abholen.</summary>
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
/// Minimaler Client für die REST-Schnittstelle von Supabase (PostgREST). Row Level Security gibt nur die Zeilen
/// des Sync-Codes bzw. des angemeldeten Nutzers frei (siehe supabase/migrations).
/// </summary>
public sealed class SupabaseVisitsClient(HttpClient http, SupabaseOptions options)
{
    public const int PageSize = 1000;

    private const string Columns = "category_id,place_id,visited_at,note,deleted,updated_at,synced_at";

    public bool IsConfigured => options.IsConfigured;

    /// <summary>Lädt Änderungen hoch. Ältere Stände überschreiben auf dem Server keine neueren (Trigger).</summary>
    public async Task PushAsync(SyncTarget target, string? accessToken, IReadOnlyList<VisitChange> changes)
    {
        var rows = changes
            .Select(c => new VisitPushRow(
                (target as SyncTarget.Code)?.Key, (target as SyncTarget.Account)?.UserId,
                c.CategoryId, c.PlaceId, c.Visit?.VisitedAt, c.Visit?.Note, c.Visit is null, c.UpdatedAt))
            .ToList();

        var (table, ownerColumn) = Table(target);
        using var request = CreateRequest(HttpMethod.Post, $"{table}?on_conflict={ownerColumn},category_id,place_id", target, accessToken);
        request.Headers.Add("Prefer", "resolution=merge-duplicates,return=minimal");
        request.Content = JsonContent.Create(rows, PlacebookJsonContext.Default.ListVisitPushRow);

        using var response = await http.SendAsync(request);
        await EnsureSuccessAsync(response);
    }

    /// <summary>Holt eine Seite von Änderungen, die der Server ab <paramref name="since"/> gespeichert hat.</summary>
    public async Task<List<VisitPullRow>> PullAsync(SyncTarget target, string? accessToken, DateTimeOffset? since, int offset)
    {
        var query = $"{Table(target).Table}?select={Columns}&order=synced_at.asc,category_id.asc,place_id.asc&limit={PageSize}&offset={offset}";
        if (since is { } value)
            query += "&synced_at=gte." + Uri.EscapeDataString(
                value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ", CultureInfo.InvariantCulture));

        using var request = CreateRequest(HttpMethod.Get, query, target, accessToken);
        using var response = await http.SendAsync(request);
        await EnsureSuccessAsync(response);
        // Als String statt gestreamt einlesen – siehe CategoryService.LoadPlacesAsync
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize(json, PlacebookJsonContext.Default.ListVisitPullRow) ?? [];
    }

    private static (string Table, string OwnerColumn) Table(SyncTarget target) => target switch
    {
        SyncTarget.Account => ("account_visits", "user_id"),
        _ => ("visits", "sync_key")
    };

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativeUrl, SyncTarget target, string? accessToken)
    {
        var request = new HttpRequestMessage(method, $"{options.Url!.TrimEnd('/')}/rest/v1/{relativeUrl}");
        request.Headers.Add("apikey", options.PublishableKey);
        switch (target)
        {
            case SyncTarget.Code code:
                request.Headers.Add("x-sync-key", code.Key.ToString());
                break;
            case SyncTarget.Account:
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                break;
        }
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync();
        throw new HttpRequestException($"Supabase antwortet mit {(int)response.StatusCode}: {body}", null, response.StatusCode);
    }
}
