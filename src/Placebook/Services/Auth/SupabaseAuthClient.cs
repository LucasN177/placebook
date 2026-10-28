using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Placebook.Services.Auth;

/// <summary>Minimaler Client für die REST-Schnittstelle von Supabase Auth (GoTrue), E-Mail + Passwort.</summary>
public sealed class SupabaseAuthClient(HttpClient http, SupabaseOptions options)
{
    public bool IsConfigured => options.IsConfigured;

    public Task<AuthTokenResponse> SignInWithPasswordAsync(string email, string password) =>
        SendAsync(HttpMethod.Post, "token?grant_type=password", new AuthCredentials(email, password),
            Json.AuthCredentials, Json.AuthTokenResponse);

    /// <summary>Ohne E-Mail-Bestätigung enthält die Antwort direkt eine Sitzung, sonst nur den Nutzer.</summary>
    public Task<AuthTokenResponse> SignUpAsync(string email, string password, string redirectTo) =>
        SendAsync(HttpMethod.Post, $"signup?redirect_to={Uri.EscapeDataString(redirectTo)}",
            new AuthCredentials(email, password), Json.AuthCredentials, Json.AuthTokenResponse);

    public Task<AuthTokenResponse> RefreshAsync(string refreshToken) =>
        SendAsync(HttpMethod.Post, "token?grant_type=refresh_token", new AuthRefreshRequest(refreshToken),
            Json.AuthRefreshRequest, Json.AuthTokenResponse);

    public Task<AuthUserResponse> GetUserAsync(string accessToken) =>
        SendAsync<object, AuthUserResponse>(HttpMethod.Get, "user", null, null, Json.AuthUserResponse, accessToken);

    public Task<AuthUserResponse> UpdatePasswordAsync(string accessToken, string password) =>
        SendAsync(HttpMethod.Put, "user", new AuthPasswordRequest(password), Json.AuthPasswordRequest,
            Json.AuthUserResponse, accessToken);

    /// <summary>Verschickt die Mail zum Zurücksetzen des Passworts.</summary>
    public Task RecoverAsync(string email, string redirectTo) =>
        SendAsync<AuthEmailRequest, object>(HttpMethod.Post, $"recover?redirect_to={Uri.EscapeDataString(redirectTo)}",
            new AuthEmailRequest(email), Json.AuthEmailRequest, null);

    /// <summary>Beendet die Sitzung auf dem Server (macht das Refresh-Token ungültig).</summary>
    public Task SignOutAsync(string accessToken) =>
        SendAsync<object, object>(HttpMethod.Post, "logout?scope=local", null, null, null, accessToken);

    private async Task<TResponse> SendAsync<TRequest, TResponse>(HttpMethod method, string path, TRequest? body,
        JsonTypeInfo<TRequest>? requestType, JsonTypeInfo<TResponse>? responseType, string? accessToken = null)
    {
        using var request = new HttpRequestMessage(method, $"{options.Url!.TrimEnd('/')}/auth/v1/{path}");
        request.Headers.Add("apikey", options.PublishableKey);
        if (accessToken is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null && requestType is not null)
            request.Content = JsonContent.Create(body, requestType);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request);
        }
        catch (HttpRequestException)
        {
            throw new AuthException("Server nicht erreichbar – bitte prüfe deine Internetverbindung.", isNetworkError: true);
        }

        using (response)
        {
            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw ToException(json, (int)response.StatusCode);
            return responseType is null || string.IsNullOrWhiteSpace(json)
                ? default!
                : JsonSerializer.Deserialize(json, responseType)!;
        }
    }

    private static AuthException ToException(string json, int statusCode)
    {
        AuthErrorResponse? error = null;
        try
        {
            error = JsonSerializer.Deserialize(json, Json.AuthErrorResponse);
        }
        catch (JsonException)
        {
        }

        var message = error?.Msg ?? error?.Message ?? error?.ErrorDescription ?? error?.Error;
        // Ältere Auth-Versionen liefern keinen error_code, sondern nur "invalid_grant" + Beschreibung
        var code = error?.ErrorCode
                   ?? (message?.Contains("Invalid login credentials", StringComparison.OrdinalIgnoreCase) == true
                       ? "invalid_credentials"
                       : error?.Error);
        return new AuthException(AuthText.Describe(code, message), code, statusCode: statusCode);
    }

    private static PlacebookJsonContext Json => PlacebookJsonContext.Default;
}
