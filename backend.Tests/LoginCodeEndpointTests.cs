using System.Net;
using System.Net.Http.Json;
using backend.Data;
using backend.DTOs;
using backend.Models;
using backend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace backend.Tests;

/*
 * Test cases:
 * 1. Password login for a verified user returns a challenge, no auth cookies, and emails a 6-digit code.
 * 2. An unverified user still gets cookies straight away (code step is only for verified emails).
 * 3. Only a hash of the code is stored, never the code.
 * 4. The correct code sets the HttpOnly cookies and the session works.
 * 5. A code cannot be used twice.
 * 6. A wrong code returns 401 and sets no cookies.
 * 7. An expired code returns 401.
 * 8. After the attempt cap, even the correct code is rejected.
 * 9. Wrong codes count toward account lockout.
 * 10. An unknown challenge id returns 401.
 * 11. A code from one user's challenge does not open another user's challenge.
 * 12. Resend inside the cooldown returns 429.
 * 13. Resend after the cooldown emails a new code and the old one stops working.
 * 14. Verify-code is rate limited.
 * 15. Resend-code is rate limited.
 */
public sealed class LoginCodeEndpointTests : IClassFixture<AuthWebApplicationFactory>
{
    private const string Password = "Password1!";

    private readonly AuthWebApplicationFactory _factory;

    public LoginCodeEndpointTests(AuthWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_ForVerifiedUser_ReturnsChallengeAndEmailsCode()
    {
        var (client, email) = await VerifiedUserAsync(_factory);

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var challenge = await response.Content.ReadFromJsonAsync<LoginChallengeResponse>();
        Assert.NotNull(challenge);
        Assert.NotEqual(Guid.Empty, challenge.ChallengeId);
        Assert.Matches(@"^\d{6}$", CapturingEmailSender.CodeFrom(_factory.Emails.LastTo(email)));
    }

    [Fact]
    public async Task Login_ForUnverifiedUser_SetsCookiesWithoutCode()
    {
        var client = _factory.CreateClient();
        var email = await RegisterAsync(client);

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Login_StoresOnlyTheCodeHash()
    {
        var (client, email) = await VerifiedUserAsync(_factory);
        var (challengeId, code) = await StartChallengeAsync(client, _factory, email);

        using var scope = _factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .LoginChallenges.SingleAsync(row => row.Id == challengeId);
        Assert.NotEqual(code, stored.CodeHash);
        Assert.DoesNotContain(code, stored.CodeHash);
    }

    [Fact]
    public async Task VerifyCode_WithCorrectCode_SetsCookiesAndSessionWorks()
    {
        var (client, email) = await VerifiedUserAsync(_factory);
        var (challengeId, code) = await StartChallengeAsync(client, _factory, email);

        var response = await client.PostAsJsonAsync(
            "/api/auth/login/verify-code", new VerifyLoginCodeRequest(challengeId, code));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var setCookie = string.Join("\n", response.Headers.GetValues("Set-Cookie"));
        Assert.Contains($"{AuthCookie.AccessTokenName}=", setCookie);
        Assert.Contains($"{AuthCookie.RefreshTokenName}=", setCookie);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);

        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task VerifyCode_CannotBeReused()
    {
        var (client, email) = await VerifiedUserAsync(_factory);
        var (challengeId, code) = await StartChallengeAsync(client, _factory, email);
        var request = new VerifyLoginCodeRequest(challengeId, code);
        await client.PostAsJsonAsync("/api/auth/login/verify-code", request);

        var second = await client.PostAsJsonAsync("/api/auth/login/verify-code", request);

        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
    }

    [Fact]
    public async Task VerifyCode_WithWrongCode_ReturnsUnauthorizedWithoutCookies()
    {
        var (client, email) = await VerifiedUserAsync(_factory);
        var (challengeId, code) = await StartChallengeAsync(client, _factory, email);

        var response = await client.PostAsJsonAsync(
            "/api/auth/login/verify-code", new VerifyLoginCodeRequest(challengeId, WrongCode(code)));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task VerifyCode_WhenExpired_ReturnsUnauthorized()
    {
        var (client, email) = await VerifiedUserAsync(_factory);
        var (challengeId, code) = await StartChallengeAsync(client, _factory, email);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.LoginChallenges.SingleAsync(row => row.Id == challengeId)).ExpiresAt =
                DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync(
            "/api/auth/login/verify-code", new VerifyLoginCodeRequest(challengeId, code));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task VerifyCode_AfterAttemptCap_RejectsEvenTheCorrectCode()
    {
        var (client, email) = await VerifiedUserAsync(_factory);
        var (challengeId, code) = await StartChallengeAsync(client, _factory, email);
        for (var i = 0; i < 5; i++)
        {
            await client.PostAsJsonAsync(
                "/api/auth/login/verify-code", new VerifyLoginCodeRequest(challengeId, WrongCode(code)));
        }

        var response = await client.PostAsJsonAsync(
            "/api/auth/login/verify-code", new VerifyLoginCodeRequest(challengeId, code));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task VerifyCode_WrongCodesCountTowardLockout()
    {
        var (client, email) = await VerifiedUserAsync(_factory);
        var (challengeId, code) = await StartChallengeAsync(client, _factory, email);
        for (var i = 0; i < 5; i++)
        {
            await client.PostAsJsonAsync(
                "/api/auth/login/verify-code", new VerifyLoginCodeRequest(challengeId, WrongCode(code)));
        }

        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));

        Assert.Equal(HttpStatusCode.Locked, login.StatusCode);
    }

    [Fact]
    public async Task VerifyCode_WithUnknownChallenge_ReturnsUnauthorized()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(
            "/api/auth/login/verify-code", new VerifyLoginCodeRequest(Guid.NewGuid(), "123456"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task VerifyCode_CodeFromAnotherUsersChallenge_IsRejected()
    {
        var (clientA, emailA) = await VerifiedUserAsync(_factory);
        var (clientB, emailB) = await VerifiedUserAsync(_factory);
        var (_, codeA) = await StartChallengeAsync(clientA, _factory, emailA);
        var (challengeB, _) = await StartChallengeAsync(clientB, _factory, emailB);

        var response = await clientB.PostAsJsonAsync(
            "/api/auth/login/verify-code", new VerifyLoginCodeRequest(challengeB, codeA));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ResendCode_InsideCooldown_ReturnsTooManyRequests()
    {
        await using var factory = new LoginCodeCooldownWebApplicationFactory();
        var (client, email) = await VerifiedUserAsync(factory);
        var (challengeId, _) = await StartChallengeAsync(client, factory, email);

        var response = await client.PostAsJsonAsync(
            "/api/auth/login/resend-code", new ResendLoginCodeRequest(challengeId));

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task ResendCode_AfterCooldown_EmailsNewCodeAndOldOneStopsWorking()
    {
        var (client, email) = await VerifiedUserAsync(_factory);
        var (challengeId, oldCode) = await StartChallengeAsync(client, _factory, email);

        var resend = await client.PostAsJsonAsync(
            "/api/auth/login/resend-code", new ResendLoginCodeRequest(challengeId));
        Assert.Equal(HttpStatusCode.Accepted, resend.StatusCode);
        var newCode = CapturingEmailSender.CodeFrom(_factory.Emails.LastTo(email));
        // A one-in-a-million collision would make this check meaningless, so skip it then.
        if (newCode != oldCode)
        {
            var old = await client.PostAsJsonAsync(
                "/api/auth/login/verify-code", new VerifyLoginCodeRequest(challengeId, oldCode));
            Assert.Equal(HttpStatusCode.Unauthorized, old.StatusCode);
        }

        var fresh = await client.PostAsJsonAsync(
            "/api/auth/login/verify-code", new VerifyLoginCodeRequest(challengeId, newCode));
        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
    }

    [Fact]
    public async Task VerifyCode_IsRateLimited()
    {
        await using var factory = new RateLimitedAuthWebApplicationFactory();
        var client = factory.CreateClient();
        HttpStatusCode last = default;
        for (var i = 0; i < 5; i++)
        {
            last = (await client.PostAsJsonAsync(
                "/api/auth/login/verify-code", new VerifyLoginCodeRequest(Guid.NewGuid(), "123456"))).StatusCode;
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }

    [Fact]
    public async Task ResendCode_IsRateLimited()
    {
        await using var factory = new RateLimitedAuthWebApplicationFactory();
        var client = factory.CreateClient();
        HttpStatusCode last = default;
        for (var i = 0; i < 4; i++)
        {
            last = (await client.PostAsJsonAsync(
                "/api/auth/login/resend-code", new ResendLoginCodeRequest(Guid.NewGuid()))).StatusCode;
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }

    private static string WrongCode(string code) => code == "000000" ? "000001" : "000000";

    private static async Task<string> RegisterAsync(HttpClient client)
    {
        var email = $"code.{Guid.NewGuid():N}@example.com";
        var response = await client.PostAsJsonAsync(
            "/api/auth/register", new RegisterRequest("Jane", "Doe", email, Password));
        response.EnsureSuccessStatusCode();
        return email;
    }

    private static async Task<(HttpClient Client, string Email)> VerifiedUserAsync(
        AuthWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        var email = await RegisterAsync(client);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Users.SingleAsync(user => user.Email == email)).EmailVerifiedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return (client, email);
    }

    private static async Task<(Guid ChallengeId, string Code)> StartChallengeAsync(
        HttpClient client, AuthWebApplicationFactory factory, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var challenge = await response.Content.ReadFromJsonAsync<LoginChallengeResponse>();
        return (challenge!.ChallengeId, CapturingEmailSender.CodeFrom(factory.Emails.LastTo(email)));
    }
}
