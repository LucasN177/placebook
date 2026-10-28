using System.Text.Json.Serialization;

namespace Placebook.Services.Auth;

public sealed record AuthUser(Guid Id, string? Email, DateTimeOffset? CreatedAt);

/// <summary>
/// Angemeldete Sitzung. Wird im localStorage gespeichert, damit man angemeldet bleibt; das Access-Token läuft nach
/// etwa einer Stunde ab und wird mit dem Refresh-Token erneuert (Supabase rotiert dabei das Refresh-Token).
/// </summary>
public sealed record AuthSession(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, AuthUser User)
{
    public bool ExpiresWithin(TimeSpan span, DateTimeOffset now) => ExpiresAt - now <= span;
}

/// <summary>Fehler von Supabase Auth mit einer für Nutzer lesbaren Meldung.</summary>
public sealed class AuthException(string message, string? errorCode = null, bool isNetworkError = false, int? statusCode = null)
    : Exception(message)
{
    public string? ErrorCode { get; } = errorCode;
    public bool IsNetworkError { get; } = isNetworkError;
    public int? StatusCode { get; } = statusCode;

    /// <summary>
    /// Der Server hat die Sitzung endgültig abgelehnt. Bekannte Codes, sonst jede Ablehnung mit 400/401/403
    /// (z. B. "validation_failed" bei ungültigem Refresh-Token). Netzfehler, 429 und 5xx zählen nicht – dann
    /// bleibt man angemeldet und es wird später erneut versucht.
    /// </summary>
    public bool IsSessionExpired => ErrorCode is "refresh_token_not_found" or "refresh_token_already_used"
        or "session_not_found" or "session_expired" or "invalid_grant" or "bad_jwt"
        || StatusCode is 400 or 401 or 403;
}

// --- Antworten der Supabase-Auth-REST-API (GoTrue) ---

public sealed record AuthUserResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("created_at")] DateTimeOffset? CreatedAt)
{
    public AuthUser ToUser() => new(Id, Email, CreatedAt);
}

/// <summary>
/// Antwort von /token und /signup. Bei /signup mit E-Mail-Bestätigung fehlen die Tokens und die Nutzerdaten
/// stehen direkt auf oberster Ebene.
/// </summary>
public sealed record AuthTokenResponse(
    [property: JsonPropertyName("access_token")] string? AccessToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("expires_in")] int? ExpiresIn,
    [property: JsonPropertyName("expires_at")] long? ExpiresAt,
    [property: JsonPropertyName("user")] AuthUserResponse? User,
    [property: JsonPropertyName("id")] Guid? Id,
    [property: JsonPropertyName("email")] string? Email)
{
    public bool HasSession => !string.IsNullOrEmpty(AccessToken) && !string.IsNullOrEmpty(RefreshToken) && User is not null;

    public AuthSession ToSession(DateTimeOffset now) => new(
        AccessToken!,
        RefreshToken!,
        ExpiresAt is { } unix ? DateTimeOffset.FromUnixTimeSeconds(unix) : now.AddSeconds(ExpiresIn ?? 3600),
        User!.ToUser());
}

public sealed record AuthErrorResponse(
    [property: JsonPropertyName("error_code")] string? ErrorCode,
    [property: JsonPropertyName("msg")] string? Msg,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("error_description")] string? ErrorDescription);

public sealed record AuthCredentials(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("password")] string Password);

public sealed record AuthRefreshRequest([property: JsonPropertyName("refresh_token")] string RefreshToken);

public sealed record AuthEmailRequest([property: JsonPropertyName("email")] string Email);

public sealed record AuthPasswordRequest([property: JsonPropertyName("password")] string Password);
