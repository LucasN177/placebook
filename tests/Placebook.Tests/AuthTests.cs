using System.Text.Json;
using Placebook.Services;
using Placebook.Services.Auth;

namespace Placebook.Tests;

public class AuthTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TokenResponse_FromSignIn_BecomesSession()
    {
        const string json = """
            {"access_token":"at","token_type":"bearer","expires_in":3600,"expires_at":1793188800,"refresh_token":"rt",
             "user":{"id":"11111111-2222-3333-4444-555555555555","email":"a@b.de","created_at":"2026-10-01T08:00:00Z"}}
            """;

        var response = JsonSerializer.Deserialize(json, PlacebookJsonContext.Default.AuthTokenResponse)!;
        var session = response.ToSession(Now);

        Assert.True(response.HasSession);
        Assert.Equal("rt", session.RefreshToken);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1793188800), session.ExpiresAt);
        Assert.Equal("a@b.de", session.User.Email);
    }

    [Fact]
    public void TokenResponse_FromSignUpWithConfirmation_HasNoSession()
    {
        const string json = """{"id":"11111111-2222-3333-4444-555555555555","email":"a@b.de","confirmation_sent_at":"2026-10-28T12:00:00Z"}""";

        var response = JsonSerializer.Deserialize(json, PlacebookJsonContext.Default.AuthTokenResponse)!;

        Assert.False(response.HasSession);
    }

    [Fact]
    public void TokenResponse_WithoutExpiresAt_UsesExpiresIn()
    {
        var response = new AuthTokenResponse("at", "rt", 600, null,
            new AuthUserResponse(Guid.NewGuid(), "a@b.de", null), null, null);

        Assert.Equal(Now.AddMinutes(10), response.ToSession(Now).ExpiresAt);
    }

    [Fact]
    public void Session_ExpiresWithin_ComparesAgainstMargin()
    {
        var session = new AuthSession("at", "rt", Now.AddMinutes(1), new AuthUser(Guid.NewGuid(), null, null));

        Assert.True(session.ExpiresWithin(TimeSpan.FromMinutes(2), Now));
        Assert.False(session.ExpiresWithin(TimeSpan.FromSeconds(30), Now));
    }

    [Fact]
    public void Session_RoundTripsThroughStorage()
    {
        var session = new AuthSession("at", "rt", Now, new AuthUser(Guid.NewGuid(), "a@b.de", Now));

        var json = JsonSerializer.Serialize(session, PlacebookJsonContext.Default.AuthSession);

        Assert.Equal(session, JsonSerializer.Deserialize(json, PlacebookJsonContext.Default.AuthSession));
    }

    [Fact]
    public void Redirect_Recovery_IsParsedFromFragment()
    {
        var redirect = AuthRedirect.Parse(
            "https://x.github.io/placebook/account#access_token=at&expires_at=1793188800&expires_in=3600&refresh_token=rt&token_type=bearer&type=recovery");

        Assert.NotNull(redirect);
        Assert.Equal(AuthRedirectKind.Recovery, redirect.Kind);
        Assert.Equal("at", redirect.AccessToken);
        Assert.Equal("rt", redirect.RefreshToken);
        Assert.Equal(1793188800, redirect.ExpiresAt);
    }

    [Fact]
    public void Redirect_Signup_IsSignedIn()
    {
        Assert.Equal(AuthRedirectKind.SignedIn,
            AuthRedirect.Parse("https://x/account#access_token=at&refresh_token=rt&type=signup")!.Kind);
    }

    [Fact]
    public void Redirect_Error_KeepsCodeAndDescription()
    {
        var redirect = AuthRedirect.Parse(
            "https://x/account#error=access_denied&error_code=otp_expired&error_description=Email+link+is+invalid+or+has+expired");

        Assert.Equal(AuthRedirectKind.Error, redirect!.Kind);
        Assert.Equal("otp_expired", redirect.ErrorCode);
        Assert.Equal("Email link is invalid or has expired", redirect.ErrorDescription);
    }

    [Theory]
    [InlineData("https://x/settings")]
    [InlineData("https://x/settings#sync=11111111-1111-1111-1111-111111111111")]
    public void Redirect_OtherUrls_AreIgnored(string uri)
    {
        Assert.Null(AuthRedirect.Parse(uri));
    }

    [Theory]
    [InlineData("invalid_credentials", "E-Mail oder Passwort ist falsch.")]
    [InlineData("user_already_exists", "Für diese E-Mail-Adresse gibt es schon ein Konto. Melde dich stattdessen an.")]
    public void Describe_TranslatesKnownErrors(string code, string expected)
    {
        Assert.Equal(expected, AuthText.Describe(code, "english text"));
    }

    [Fact]
    public void Describe_UnknownError_IncludesServerMessage()
    {
        Assert.Contains("Something odd", AuthText.Describe("whatever", "Something odd"));
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("12345", "12345", false)]
    [InlineData("123456", "123457", false)]
    [InlineData("123456", "123456", true)]
    public void ValidateNewPassword_ChecksLengthAndConfirmation(string? password, string? confirmation, bool valid)
    {
        Assert.Equal(valid, AuthText.ValidateNewPassword(password, confirmation) is null);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("kein-at", false)]
    [InlineData("a@b.de", true)]
    public void ValidateEmail_RequiresAddress(string email, bool valid)
    {
        Assert.Equal(valid, AuthText.ValidateEmail(email) is null);
    }

    [Theory]
    [InlineData("refresh_token_not_found", null, true)]
    [InlineData("refresh_token_already_used", null, true)]
    [InlineData("validation_failed", 400, true)] // so antwortet Supabase auf ein ungültiges Refresh-Token
    [InlineData(null, 401, true)]
    [InlineData("over_request_rate_limit", 429, false)]
    [InlineData(null, 503, false)]
    [InlineData(null, null, false)]
    public void AuthException_DetectsExpiredSession(string? code, int? status, bool expired)
    {
        Assert.Equal(expired, new AuthException("x", code, statusCode: status).IsSessionExpired);
    }
}
