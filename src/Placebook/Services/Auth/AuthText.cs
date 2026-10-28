namespace Placebook.Services.Auth;

/// <summary>Übersetzt Fehler von Supabase Auth in verständliche deutsche Meldungen.</summary>
public static class AuthText
{
    public const int MinPasswordLength = 6;

    public static string Describe(string? errorCode, string? serverMessage) => errorCode switch
    {
        "invalid_credentials" => "E-Mail oder Passwort ist falsch.",
        "email_not_confirmed" => "Bitte bestätige zuerst deine E-Mail-Adresse – der Link steht in der Bestätigungsmail.",
        "user_already_exists" or "email_exists" => "Für diese E-Mail-Adresse gibt es schon ein Konto. Melde dich stattdessen an.",
        "weak_password" => $"Das Passwort ist zu schwach – bitte mindestens {MinPasswordLength} Zeichen verwenden.",
        "same_password" => "Das neue Passwort muss sich vom bisherigen unterscheiden.",
        "email_address_invalid" or "validation_failed" => "Bitte gib eine gültige E-Mail-Adresse ein.",
        "signup_disabled" or "email_provider_disabled" => "Die Registrierung ist derzeit deaktiviert.",
        "over_email_send_rate_limit" or "over_request_rate_limit" => "Zu viele Versuche – bitte warte kurz und versuche es dann erneut.",
        "refresh_token_not_found" or "refresh_token_already_used" or "session_not_found" or "session_expired"
            => "Deine Anmeldung ist abgelaufen – bitte melde dich erneut an.",
        _ when serverMessage is { Length: > 0 } => $"Das hat nicht geklappt: {serverMessage}",
        _ => "Das hat nicht geklappt. Bitte versuche es erneut."
    };

    /// <summary>Prüft Eingaben vor dem Absenden; null bedeutet gültig.</summary>
    public static string? ValidateEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? "Bitte gib deine E-Mail-Adresse ein."
        : !email.Contains('@') || email.Trim().Length < 3 ? "Bitte gib eine gültige E-Mail-Adresse ein."
        : null;

    public static string? ValidateNewPassword(string? password, string? confirmation) =>
        string.IsNullOrEmpty(password) || password.Length < MinPasswordLength
            ? $"Das Passwort muss mindestens {MinPasswordLength} Zeichen lang sein."
        : password != confirmation ? "Die Passwörter stimmen nicht überein."
        : null;
}
