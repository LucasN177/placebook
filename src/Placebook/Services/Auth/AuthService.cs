using System.Text.Json;
using Microsoft.JSInterop;

namespace Placebook.Services.Auth;

/// <summary>
/// Anmeldung über Supabase Auth (E-Mail + Passwort). Die Sitzung wird im localStorage gespeichert, damit man
/// angemeldet bleibt, und vor Ablauf des Access-Tokens automatisch erneuert.
/// </summary>
public sealed class AuthService(SupabaseAuthClient client, IJSRuntime js, TimeProvider time, Microsoft.AspNetCore.Components.NavigationManager navigation)
    : IAsyncDisposable
{
    private const string StorageKey = "placebook.auth.session";

    // Rechtzeitig vor Ablauf erneuern; abgelaufene Tokens lehnt Supabase ab
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(2);

    private Task? _initializing;
    private Task<AuthSession?>? _refreshing;
    private ITimer? _refreshTimer;

    public AuthSession? Session { get; private set; }
    public AuthUser? User => Session?.User;
    public bool IsLoggedIn => Session is not null;
    public bool IsConfigured => client.IsConfigured;

    /// <summary>Wird bei An- und Abmeldung ausgelöst (nicht bei bloßer Token-Erneuerung).</summary>
    public event Action? Changed;

    /// <summary>Lädt eine gespeicherte Sitzung; mehrfache Aufrufe warten auf denselben Ladevorgang.</summary>
    public Task InitializeAsync() => _initializing ??= LoadAsync();

    private async Task LoadAsync()
    {
        if (!client.IsConfigured) return;
        Session = Deserialize(await js.InvokeAsync<string?>("localStorage.getItem", StorageKey));
        if (Session is null) return;

        if (Session.ExpiresWithin(RefreshMargin, time.GetUtcNow()))
            await RefreshAsync(); // bei fehlendem Netz bleibt die Sitzung erhalten und wird später erneuert
        ScheduleRefresh();
        Changed?.Invoke();
    }

    public async Task SignInAsync(string email, string password)
    {
        var response = await client.SignInWithPasswordAsync(email.Trim(), password);
        await SetSessionAsync(response.ToSession(time.GetUtcNow()));
    }

    /// <returns>true, wenn man direkt angemeldet ist; false, wenn erst die E-Mail bestätigt werden muss.</returns>
    public async Task<bool> SignUpAsync(string email, string password)
    {
        var response = await client.SignUpAsync(email.Trim(), password, RedirectUrl);
        if (!response.HasSession) return false;
        await SetSessionAsync(response.ToSession(time.GetUtcNow()));
        return true;
    }

    public Task SendPasswordResetAsync(string email) => client.RecoverAsync(email.Trim(), RedirectUrl);

    public async Task UpdatePasswordAsync(string newPassword)
    {
        var token = await GetAccessTokenAsync() ?? throw new AuthException(AuthText.Describe("session_expired", null), "session_expired");
        await client.UpdatePasswordAsync(token, newPassword);
    }

    /// <summary>Meldet ab. Lokal immer, auf dem Server nach Möglichkeit (macht das Refresh-Token ungültig).</summary>
    public async Task SignOutAsync()
    {
        if (Session is { } session)
        {
            try
            {
                await client.SignOutAsync(session.AccessToken);
            }
            catch (AuthException)
            {
                // Offline oder Token schon abgelaufen – lokal abmelden reicht dann
            }
        }
        await SetSessionAsync(null);
    }

    /// <summary>Gültiges Access-Token für Datenbank-Anfragen; erneuert es bei Bedarf.</summary>
    public async Task<string?> GetAccessTokenAsync()
    {
        await InitializeAsync();
        if (Session is null) return null;
        if (Session.ExpiresWithin(RefreshMargin, time.GetUtcNow()))
            await RefreshAsync();
        return Session?.AccessToken;
    }

    /// <summary>Erneuert die Sitzung, z. B. nachdem der Server ein Token abgelehnt hat.</summary>
    public async Task<AuthSession?> RefreshAsync()
    {
        // Nie zwei Erneuerungen gleichzeitig: Supabase rotiert das Refresh-Token, ein zweiter Versuch mit dem
        // alten Token würde die Sitzung beenden
        if (_refreshing is { } running) return await running;
        _refreshing = RefreshCoreAsync();
        try
        {
            return await _refreshing;
        }
        finally
        {
            _refreshing = null;
        }
    }

    private async Task<AuthSession?> RefreshCoreAsync()
    {
        try
        {
            if (Session is not { } session) return null;
            var response = await client.RefreshAsync(session.RefreshToken);
            var refreshed = response.ToSession(time.GetUtcNow());
            Session = refreshed;
            await SaveAsync();
            ScheduleRefresh();
            return refreshed;
        }
        catch (AuthException ex) when (ex.IsSessionExpired)
        {
            await SetSessionAsync(null);
            return null;
        }
        catch (AuthException)
        {
            return Session; // z. B. offline: alte Sitzung behalten, später erneut versuchen
        }
    }

    /// <summary>
    /// Verarbeitet die Rückkehr aus einer Supabase-Mail (Sitzung im URL-Fragment) und entfernt das Fragment aus
    /// der Adresszeile.
    /// </summary>
    public async Task<AuthRedirect?> HandleRedirectAsync(string uri)
    {
        var redirect = AuthRedirect.Parse(uri);
        if (redirect is null) return null;

        await js.InvokeVoidAsync("history.replaceState", null, "", uri[..uri.IndexOf('#')]);
        if (redirect.Kind == AuthRedirectKind.Error) return redirect;

        var user = await client.GetUserAsync(redirect.AccessToken!);
        var now = time.GetUtcNow();
        var expiresAt = redirect.ExpiresAt is { } unix
            ? DateTimeOffset.FromUnixTimeSeconds(unix)
            : now.AddSeconds(redirect.ExpiresIn ?? 3600);
        await SetSessionAsync(new AuthSession(redirect.AccessToken!, redirect.RefreshToken!, expiresAt, user.ToUser()));
        return redirect;
    }

    /// <summary>Hierhin führen Links aus Supabase-Mails zurück (muss in Supabase als Redirect-URL erlaubt sein).</summary>
    private string RedirectUrl => $"{navigation.BaseUri}account";

    private async Task SetSessionAsync(AuthSession? session)
    {
        var changedUser = Session?.User.Id != session?.User.Id;
        Session = session;
        await SaveAsync();
        ScheduleRefresh();
        if (changedUser) Changed?.Invoke();
    }

    private Task SaveAsync() => Session is null
        ? js.InvokeVoidAsync("localStorage.removeItem", StorageKey).AsTask()
        : js.InvokeVoidAsync("localStorage.setItem", StorageKey,
            JsonSerializer.Serialize(Session, PlacebookJsonContext.Default.AuthSession)).AsTask();

    private void ScheduleRefresh()
    {
        _refreshTimer?.Dispose();
        _refreshTimer = null;
        if (Session is null) return;

        var due = Session.ExpiresAt - RefreshMargin - time.GetUtcNow();
        _refreshTimer = time.CreateTimer(_ => _ = RefreshAsync(), null,
            due > TimeSpan.Zero ? due : TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);
    }

    private static AuthSession? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize(json, PlacebookJsonContext.Default.AuthSession);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public ValueTask DisposeAsync()
    {
        _refreshTimer?.Dispose();
        return ValueTask.CompletedTask;
    }
}
