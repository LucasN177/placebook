namespace Placebook.Services.Auth;

public enum AuthRedirectKind { SignedIn, Recovery, Error }

/// <summary>
/// Links aus Supabase-Mails (Registrierung bestätigen, Passwort zurücksetzen) führen zurück in die App und tragen
/// die Sitzung im URL-Fragment: <c>#access_token=…&amp;refresh_token=…&amp;expires_at=…&amp;type=recovery</c>.
/// Bei Fehlern steht dort <c>#error=…&amp;error_code=…&amp;error_description=…</c>.
/// </summary>
public sealed record AuthRedirect(
    AuthRedirectKind Kind,
    string? AccessToken,
    string? RefreshToken,
    long? ExpiresAt,
    int? ExpiresIn,
    string? ErrorCode,
    string? ErrorDescription)
{
    public static AuthRedirect? Parse(string uri)
    {
        var hashIndex = uri.IndexOf('#');
        if (hashIndex < 0) return null;

        var values = uri[(hashIndex + 1)..]
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(kv => kv.Length == 2)
            .ToDictionary(kv => kv[0], kv => Uri.UnescapeDataString(kv[1].Replace('+', ' ')));

        if (values.TryGetValue("error_description", out var description) || values.ContainsKey("error"))
            return new AuthRedirect(AuthRedirectKind.Error, null, null, null, null,
                values.GetValueOrDefault("error_code"), description ?? values.GetValueOrDefault("error"));

        if (!values.TryGetValue("access_token", out var accessToken) || !values.TryGetValue("refresh_token", out var refreshToken))
            return null;

        return new AuthRedirect(
            values.GetValueOrDefault("type") == "recovery" ? AuthRedirectKind.Recovery : AuthRedirectKind.SignedIn,
            accessToken,
            refreshToken,
            long.TryParse(values.GetValueOrDefault("expires_at"), out var expiresAt) ? expiresAt : null,
            int.TryParse(values.GetValueOrDefault("expires_in"), out var expiresIn) ? expiresIn : null,
            null,
            null);
    }
}
