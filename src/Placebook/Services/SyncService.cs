using System.Text.Json;
using Microsoft.JSInterop;
using Placebook.Models;

namespace Placebook.Services;

public enum SyncStatus { NotConfigured, Disabled, Idle, Syncing, Error }

/// <summary>
/// Synchronisiert die Besuche mit Supabase – ohne Login, über einen geheimen Sync-Code.
/// <para>
/// localStorage bleibt der lokale Speicher: Die App funktioniert offline, Änderungen landen in einer Warteschlange
/// und werden hochgeladen, sobald der Server erreichbar ist. Danach werden Änderungen anderer Geräte mit demselben
/// Sync-Code abgeholt. Pro Ort gewinnt der neueste Stand.
/// </para>
/// </summary>
public sealed class SyncService(VisitService visits, SupabaseVisitsClient client, IJSRuntime js, TimeProvider time)
    : IAsyncDisposable
{
    private const string KeyStorageKey = "placebook.sync.key";
    private const string EnabledStorageKey = "placebook.sync.enabled";
    private const string PendingStorageKey = "placebook.sync.pending";
    private const string SinceStorageKey = "placebook.sync.since";

    // Der Server vergibt synced_at beim Start der Transaktion; gleichzeitig laufende Schreibvorgänge können
    // daher minimal "in der Vergangenheit" landen. Mit Überlappung abholen – doppelt anwenden ist harmlos.
    private static readonly TimeSpan PullOverlap = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan PushDelay = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan PeriodicInterval = TimeSpan.FromMinutes(2);
    private const int PushBatchSize = 500;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<VisitChange> _pending = [];
    private DateTimeOffset? _since;
    private ITimer? _pushTimer;
    private ITimer? _periodicTimer;
    private bool _started;

    public SyncStatus Status { get; private set; } = SyncStatus.NotConfigured;
    public Guid? SyncKey { get; private set; }
    public DateTimeOffset? LastSuccess { get; private set; }
    public string? LastError { get; private set; }
    public int PendingCount => _pending.Count;

    public event Action? StatusChanged;

    /// <summary>Einmal beim App-Start aufrufen. Erzeugt beim ersten Mal einen Sync-Code und lädt alles hoch.</summary>
    public async Task StartAsync()
    {
        if (_started) return;
        _started = true;
        if (!client.IsConfigured) return;

        await visits.EnsureLoadedAsync();
        _pending = Deserialize(await GetItemAsync(PendingStorageKey)) ?? [];
        _since = DateTimeOffset.TryParse(await GetItemAsync(SinceStorageKey), out var since) ? since : null;
        visits.LocalChanged += OnLocalChanged;

        if (await GetItemAsync(EnabledStorageKey) == "false")
        {
            SetStatus(SyncStatus.Disabled);
            return;
        }

        if (Guid.TryParse(await GetItemAsync(KeyStorageKey), out var key))
        {
            SyncKey = key;
            SetStatus(SyncStatus.Idle);
        }
        else
        {
            await UseKeyAsync(Guid.NewGuid());
        }

        _periodicTimer = time.CreateTimer(_ => _ = SyncNowAsync(), null, PeriodicInterval, PeriodicInterval);
        await SyncNowAsync();
    }

    /// <summary>Verbindet dieses Gerät mit dem Sync-Code eines anderen; lokale Besuche werden zusammengeführt.</summary>
    public async Task ConnectAsync(Guid key)
    {
        await SetEnabledAsync(true);
        await UseKeyAsync(key);
        await SyncNowAsync();
    }

    public async Task SetEnabledAsync(bool enabled)
    {
        if (!client.IsConfigured) return;
        await js.InvokeVoidAsync("localStorage.setItem", EnabledStorageKey, enabled ? "true" : "false");
        if (!enabled)
        {
            _periodicTimer?.Dispose();
            _periodicTimer = null;
            SetStatus(SyncStatus.Disabled);
            return;
        }

        if (SyncKey is null)
        {
            if (Guid.TryParse(await GetItemAsync(KeyStorageKey), out var key)) SyncKey = key;
            else await UseKeyAsync(Guid.NewGuid());
        }
        _periodicTimer ??= time.CreateTimer(_ => _ = SyncNowAsync(), null, PeriodicInterval, PeriodicInterval);
        SetStatus(SyncStatus.Idle);
        await SyncNowAsync();
    }

    /// <summary>Lädt ausstehende Änderungen hoch und holt danach neue Änderungen ab.</summary>
    public async Task SyncNowAsync()
    {
        if (!client.IsConfigured || SyncKey is not { } key || Status == SyncStatus.Disabled) return;
        if (!await _gate.WaitAsync(0)) return; // läuft bereits

        try
        {
            SetStatus(SyncStatus.Syncing);
            await PushPendingAsync(key);
            await PullAsync(key);
            LastSuccess = time.GetUtcNow();
            LastError = null;
            SetStatus(SyncStatus.Idle);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            LastError = DescribeError(ex);
            Console.Error.WriteLine($"Synchronisierung fehlgeschlagen: {ex.Message}");
            SetStatus(SyncStatus.Error);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task PushPendingAsync(Guid key)
    {
        while (_pending.Count > 0)
        {
            var batch = _pending.Take(PushBatchSize).ToList();
            await client.PushAsync(key, batch);
            // Nur entfernen, was hochgeladen wurde – währenddessen eingetroffene neuere Änderungen bleiben
            _pending = _pending.Where(p => !batch.Any(b => b == p)).ToList();
            await SavePendingAsync();
        }
    }

    private async Task PullAsync(Guid key)
    {
        var since = _since - PullOverlap;
        var rows = new List<VisitPullRow>();
        for (var offset = 0; ; offset += SupabaseVisitsClient.PageSize)
        {
            var page = await client.PullAsync(key, since, offset);
            rows.AddRange(page);
            if (page.Count < SupabaseVisitsClient.PageSize) break;
        }
        if (rows.Count == 0) return;

        var toApply = SyncMerge.SelectRemote(rows.Select(r => r.ToChange()), visits.GetVisit, _pending);
        await visits.ApplyRemoteAsync(toApply);

        _since = rows.Max(r => r.SyncedAt);
        await js.InvokeVoidAsync("localStorage.setItem", SinceStorageKey, _since.Value.ToString("O"));
    }

    private static string DescribeError(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: null } or TaskCanceledException
            => "Server nicht erreichbar – Änderungen werden später hochgeladen.",
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound }
            => "Die Tabelle „visits“ fehlt in Supabase – Migration supabase/migrations/001_visits.sql ausführen.",
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden }
            => "Supabase verweigert den Zugriff – Publishable Key und Row-Level-Security-Regel prüfen.",
        HttpRequestException { StatusCode: { } code }
            => $"Supabase-Fehler {(int)code} – Änderungen bleiben lokal gespeichert.",
        _ => "Antwort von Supabase nicht lesbar – Änderungen bleiben lokal gespeichert."
    };

    private void OnLocalChanged(IReadOnlyList<VisitChange> changes)
    {
        _pending = SyncMerge.Enqueue(_pending, changes);
        _ = SavePendingAsync();
        StatusChanged?.Invoke();
        if (SyncKey is null || Status == SyncStatus.Disabled) return;

        // Mehrere schnelle Änderungen gesammelt hochladen
        _pushTimer?.Dispose();
        _pushTimer = time.CreateTimer(_ => _ = SyncNowAsync(), null, PushDelay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Neuer Code: alles erneut abholen und alle lokalen Besuche hochladen (zusammenführen).</summary>
    private async Task UseKeyAsync(Guid key)
    {
        SyncKey = key;
        _since = null;
        _pending = SyncMerge.Enqueue(_pending, visits.AllAsChanges());
        await js.InvokeVoidAsync("localStorage.setItem", KeyStorageKey, key.ToString());
        await js.InvokeVoidAsync("localStorage.removeItem", SinceStorageKey);
        await SavePendingAsync();
        SetStatus(SyncStatus.Idle);
    }

    private Task SavePendingAsync() =>
        js.InvokeVoidAsync("localStorage.setItem", PendingStorageKey,
            JsonSerializer.Serialize(_pending, PlacebookJsonContext.Default.ListVisitChange)).AsTask();

    private Task<string?> GetItemAsync(string key) => js.InvokeAsync<string?>("localStorage.getItem", key).AsTask();

    private static List<VisitChange>? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize(json, PlacebookJsonContext.Default.ListVisitChange);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void SetStatus(SyncStatus status)
    {
        Status = status;
        StatusChanged?.Invoke();
    }

    public ValueTask DisposeAsync()
    {
        visits.LocalChanged -= OnLocalChanged;
        _pushTimer?.Dispose();
        _periodicTimer?.Dispose();
        return ValueTask.CompletedTask;
    }
}
