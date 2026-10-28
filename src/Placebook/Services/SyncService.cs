using System.Net;
using System.Text.Json;
using Microsoft.JSInterop;
using Placebook.Models;
using Placebook.Services.Auth;

namespace Placebook.Services;

public enum SyncStatus { NotConfigured, Disabled, Idle, Syncing, Error }

/// <summary>
/// Synchronisiert die Besuche mit Supabase – angemeldet in das eigene Konto, sonst über einen geheimen Sync-Code.
/// <para>
/// localStorage bleibt der lokale Speicher: Die App funktioniert offline, Änderungen landen in einer Warteschlange
/// und werden hochgeladen, sobald der Server erreichbar ist. Danach werden Änderungen anderer Geräte abgeholt.
/// Pro Ort gewinnt der neueste Stand. Wechselt das Ziel (Anmelden, anderes Gerät verbinden), werden alle lokalen
/// Besuche in das neue Ziel übernommen.
/// </para>
/// </summary>
public sealed class SyncService(
    VisitService visits, SupabaseVisitsClient client, AuthService auth, IJSRuntime js, TimeProvider time)
    : IAsyncDisposable
{
    private const string KeyStorageKey = "placebook.sync.key";
    private const string EnabledStorageKey = "placebook.sync.enabled";
    private const string PendingStorageKey = "placebook.sync.pending";
    private const string SinceStorageKey = "placebook.sync.since";
    private const string TargetStorageKey = "placebook.sync.target";

    // Der Server vergibt synced_at beim Start der Transaktion; gleichzeitig laufende Schreibvorgänge können
    // daher minimal "in der Vergangenheit" landen. Mit Überlappung abholen – doppelt anwenden ist harmlos.
    private static readonly TimeSpan PullOverlap = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan PushDelay = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan PeriodicInterval = TimeSpan.FromMinutes(2);
    private const int PushBatchSize = 500;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<VisitChange> _pending = [];
    private DateTimeOffset? _since;
    private string? _lastTargetId;
    private bool _codeEnabled = true;
    private ITimer? _pushTimer;
    private ITimer? _periodicTimer;
    private Task? _starting;
    private bool _signingOut;

    public SyncStatus Status { get; private set; } = SyncStatus.NotConfigured;

    /// <summary>Sync-Code für die Nutzung ohne Anmeldung.</summary>
    public Guid? SyncKey { get; private set; }

    public DateTimeOffset? LastSuccess { get; private set; }
    public string? LastError { get; private set; }
    public int PendingCount => _pending.Count;

    /// <summary>Angemeldet: Es wird in das Konto synchronisiert, der Sync-Code spielt keine Rolle.</summary>
    public bool UsesAccount => auth.IsLoggedIn;

    public event Action? StatusChanged;

    /// <summary>
    /// Aktuelles Ziel. Ist man nicht (mehr) angemeldet, obwohl zuletzt ins Konto synchronisiert wurde, ist die
    /// Anmeldung abgelaufen: dann pausieren statt die Kontodaten unter dem Sync-Code abzulegen.
    /// </summary>
    private SyncTarget? Target =>
        auth.User is { } user ? new SyncTarget.Account(user.Id)
        : _lastTargetId?.StartsWith("user:", StringComparison.Ordinal) == true ? null
        : _codeEnabled && SyncKey is { } key ? new SyncTarget.Code(key)
        : null;

    /// <summary>Einmal beim App-Start aufrufen; weitere Aufrufe warten auf denselben Start.</summary>
    public Task StartAsync() => _starting ??= StartCoreAsync();

    private async Task StartCoreAsync()
    {
        if (!client.IsConfigured) return;

        await visits.EnsureLoadedAsync();
        await auth.InitializeAsync();
        _pending = Deserialize(await GetItemAsync(PendingStorageKey)) ?? [];
        _since = DateTimeOffset.TryParse(await GetItemAsync(SinceStorageKey), out var since) ? since : null;
        _lastTargetId = await GetItemAsync(TargetStorageKey);
        _codeEnabled = await GetItemAsync(EnabledStorageKey) != "false";

        if (Guid.TryParse(await GetItemAsync(KeyStorageKey), out var key))
            SyncKey = key;
        else
            await SetKeyAsync(Guid.NewGuid());

        visits.LocalChanged += OnLocalChanged;
        auth.Changed += OnAuthChanged;
        _periodicTimer = time.CreateTimer(_ => _ = SyncNowAsync(), null, PeriodicInterval, PeriodicInterval);

        await ApplyTargetAsync();
        await SyncNowAsync();
    }

    /// <summary>Verbindet dieses Gerät (ohne Anmeldung) mit dem Sync-Code eines anderen.</summary>
    public async Task ConnectAsync(Guid key)
    {
        await StartAsync();
        _codeEnabled = true;
        await js.InvokeVoidAsync("localStorage.setItem", EnabledStorageKey, "true");
        await SetKeyAsync(key);
        await ApplyTargetAsync();
        await SyncNowAsync();
    }

    /// <summary>Schaltet die Cloud-Speicherung ohne Anmeldung ein oder aus.</summary>
    public async Task SetEnabledAsync(bool enabled)
    {
        await StartAsync();
        _codeEnabled = enabled;
        await js.InvokeVoidAsync("localStorage.setItem", EnabledStorageKey, enabled ? "true" : "false");
        await ApplyTargetAsync();
        await SyncNowAsync();
    }

    /// <summary>
    /// Lädt Ausstehendes hoch, meldet ab und entfernt die Besuche vom Gerät (sie liegen im Konto). Danach
    /// beginnt ein neuer, leerer Sync-Code – der alte könnte sonst Besuche aus der Zeit vor der Anmeldung
    /// zurückbringen.
    /// </summary>
    public async Task SignOutAsync()
    {
        await StartAsync();
        await SyncNowAsync();

        // Während des Abmeldens nicht auf das Auth-Ereignis reagieren: es würde sonst auf den alten Sync-Code
        // umschalten und die Kontobesuche dorthin hochladen, bevor sie vom Gerät entfernt sind
        _signingOut = true;
        try
        {
            await auth.SignOutAsync();
            await visits.ClearAsync();
            _pending = [];
            await SavePendingAsync();
            _lastTargetId = null; // bewusst abgemeldet – nicht als abgelaufene Anmeldung behandeln
            await SetKeyAsync(Guid.NewGuid());
            await ApplyTargetAsync();
        }
        finally
        {
            _signingOut = false;
        }
    }

    /// <summary>Gibt es Änderungen, die noch nicht hochgeladen werden konnten? (vor dem Abmelden prüfen)</summary>
    public async Task<bool> HasUnsyncedChangesAsync()
    {
        await SyncNowAsync();
        return _pending.Count > 0;
    }

    /// <summary>Lädt ausstehende Änderungen hoch und holt danach neue Änderungen ab.</summary>
    public async Task SyncNowAsync()
    {
        if (!client.IsConfigured) return;
        if (Target is not { } target)
        {
            UpdateIdleStatus();
            return;
        }
        if (!await _gate.WaitAsync(0)) return; // läuft bereits

        try
        {
            SetStatus(SyncStatus.Syncing);
            await RunWithAuthRetryAsync(target, token => PushPendingAsync(target, token));
            await RunWithAuthRetryAsync(target, token => PullAsync(target, token));
            LastSuccess = time.GetUtcNow();
            LastError = null;
            SetStatus(SyncStatus.Idle);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or AuthException)
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

    /// <summary>Im Konto-Modus bei abgelehntem Token einmal erneuern und wiederholen.</summary>
    private async Task RunWithAuthRetryAsync(SyncTarget target, Func<string?, Task> action)
    {
        if (target is not SyncTarget.Account)
        {
            await action(null);
            return;
        }

        var token = await auth.GetAccessTokenAsync() ?? throw SessionExpired();
        try
        {
            await action(token);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            var refreshed = await auth.RefreshAsync() ?? throw SessionExpired();
            await action(refreshed.AccessToken);
        }
    }

    private async Task PushPendingAsync(SyncTarget target, string? token)
    {
        while (_pending.Count > 0)
        {
            var batch = _pending.Take(PushBatchSize).ToList();
            await client.PushAsync(target, token, batch);
            // Nur entfernen, was hochgeladen wurde – währenddessen eingetroffene neuere Änderungen bleiben
            var uploaded = batch.ToHashSet();
            _pending = _pending.Where(p => !uploaded.Contains(p)).ToList();
            await SavePendingAsync();
        }
    }

    private async Task PullAsync(SyncTarget target, string? token)
    {
        var since = _since - PullOverlap;
        var rows = new List<VisitPullRow>();
        for (var offset = 0; ; offset += SupabaseVisitsClient.PageSize)
        {
            var page = await client.PullAsync(target, token, since, offset);
            rows.AddRange(page);
            if (page.Count < SupabaseVisitsClient.PageSize) break;
        }
        if (rows.Count == 0) return;

        var toApply = SyncMerge.SelectRemote(rows.Select(r => r.ToChange()), visits.GetVisit, _pending);
        await visits.ApplyRemoteAsync(toApply);

        _since = rows.Max(r => r.SyncedAt);
        await js.InvokeVoidAsync("localStorage.setItem", SinceStorageKey, _since.Value.ToString("O"));
    }

    /// <summary>
    /// Neues Ziel (erste Nutzung, Anmeldung, anderer Sync-Code): alles neu abholen und alle lokalen Besuche
    /// hochladen – so werden beide Seiten zusammengeführt.
    /// </summary>
    private async Task ApplyTargetAsync()
    {
        if (Target is { } target && target.Id != _lastTargetId)
        {
            var switchedAccount = target is SyncTarget.Account
                                  && _lastTargetId?.StartsWith("user:", StringComparison.Ordinal) == true;
            _lastTargetId = target.Id;
            _since = null;
            if (switchedAccount)
            {
                // Anderes Konto als zuletzt (vorherige Anmeldung war abgelaufen): die lokalen Besuche gehören dem
                // vorherigen Konto und liegen dort – nicht in das neue übernehmen, sondern dessen Besuche laden
                _pending = [];
                await visits.ClearAsync();
            }
            else
            {
                _pending = SyncMerge.Enqueue(_pending, visits.AllAsChanges());
            }
            await js.InvokeVoidAsync("localStorage.setItem", TargetStorageKey, target.Id);
            await js.InvokeVoidAsync("localStorage.removeItem", SinceStorageKey);
            await SavePendingAsync();
        }
        UpdateIdleStatus();
    }

    private void OnAuthChanged()
    {
        if (!_signingOut) _ = OnAuthChangedAsync();
    }

    private async Task OnAuthChangedAsync()
    {
        // Anmeldung: lokale Besuche ins Konto übernehmen. Abgelaufene Anmeldung: Ziel ist null → pausiert.
        await ApplyTargetAsync();
        await SyncNowAsync();
    }

    private void OnLocalChanged(IReadOnlyList<VisitChange> changes)
    {
        _pending = SyncMerge.Enqueue(_pending, changes);
        _ = SavePendingAsync();
        StatusChanged?.Invoke();
        if (Target is null) return;

        // Mehrere schnelle Änderungen gesammelt hochladen
        _pushTimer?.Dispose();
        _pushTimer = time.CreateTimer(_ => _ = SyncNowAsync(), null, PushDelay, Timeout.InfiniteTimeSpan);
    }

    private async Task SetKeyAsync(Guid key)
    {
        SyncKey = key;
        await js.InvokeVoidAsync("localStorage.setItem", KeyStorageKey, key.ToString());
    }

    private void UpdateIdleStatus()
    {
        if (!client.IsConfigured) SetStatus(SyncStatus.NotConfigured);
        else if (Target is null && _lastTargetId?.StartsWith("user:", StringComparison.Ordinal) == true)
        {
            LastError = AuthText.Describe("session_expired", null);
            SetStatus(SyncStatus.Error);
        }
        else if (Target is null) SetStatus(SyncStatus.Disabled);
        else if (Status is SyncStatus.NotConfigured or SyncStatus.Disabled) SetStatus(SyncStatus.Idle);
    }

    private static AuthException SessionExpired() => new(AuthText.Describe("session_expired", null), "session_expired");

    private static string DescribeError(Exception ex) => ex switch
    {
        AuthException auth => auth.Message,
        HttpRequestException { StatusCode: null } or TaskCanceledException
            => "Server nicht erreichbar – Änderungen werden später hochgeladen.",
        HttpRequestException { StatusCode: HttpStatusCode.NotFound }
            => "Eine Tabelle fehlt in Supabase – Migrationen aus supabase/migrations ausführen.",
        HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden }
            => "Supabase verweigert den Zugriff – Publishable Key und Row-Level-Security-Regeln prüfen.",
        HttpRequestException { StatusCode: { } code }
            => $"Supabase-Fehler {(int)code} – Änderungen bleiben lokal gespeichert.",
        _ => "Antwort von Supabase nicht lesbar – Änderungen bleiben lokal gespeichert."
    };

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
        auth.Changed -= OnAuthChanged;
        _pushTimer?.Dispose();
        _periodicTimer?.Dispose();
        return ValueTask.CompletedTask;
    }
}
