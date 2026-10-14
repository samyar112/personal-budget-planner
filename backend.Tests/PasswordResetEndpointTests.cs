using System.Net;
using System.Net.Http.Json;
using backend.Data;
using backend.DTOs;
using backend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace backend.Tests;

/*
 * Test cases:
 * 1. Forgot-password emails a reset link to a known address.
 * 2. Only the token hash is stored, never the raw token.
 * 3. Known and unknown addresses get the identical response, and unknown ones get no email.
 * 4. A Google-only account gets an explanatory email and no reset token.
 * 5. A valid token sets the new password: old one stops working, new one logs in.
 * 6. A token cannot be used twice.
 * 7. An expired token is rejected and the password is unchanged.
 * 8. An unknown token is rejected.
 * 9. A weak new password is rejected and the token stays usable.
 * 10. A newer request invalidates the previous link.
 * 11. A successful reset clears lockout state.
 * 12. A successful reset revokes every refresh token.
 * 13. Forgot-password is rate limited.
 * 14. Forgot-password still answers 202 when the mail server is down.
 */
public sealed class PasswordResetEndpointTests : IClassFixture<AuthWebApplicationFactory>
{
    private const string Password = "Password1!";
    private const string NewPassword = "NewPassw0rd!";

    private readonly AuthWebApplicationFactory _factory;

    public PasswordResetEndpointTests(AuthWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ForgotPassword_EmailsResetLinkToKnownAddress()
    {
        var client = CreateClient();
        var email = await RegisterAsync(client);

        var response = await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var message = _factory.Emails.LastTo(email);
        Assert.Contains("/reset-password?token=", message.TextBody);
    }

    [Fact]
    public async Task ForgotPassword_StoresOnlyTheTokenHash()
    {
        var client = CreateClient();
        var email = await RegisterAsync(client);
        var raw = await RequestResetAsync(client, email);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(candidate => candidate.Email == email);
        var stored = await db.PasswordResetTokens.SingleAsync(token => token.UserId == user.Id);

        Assert.NotEqual(raw, stored.TokenHash);
        Assert.DoesNotContain(raw, stored.TokenHash);
        Assert.True(stored.ExpiresAt > DateTime.UtcNow);
        Assert.Null(stored.UsedAt);
    }

    [Fact]
    public async Task ForgotPassword_AnswersIdenticallyForKnownAndUnknownAddresses()
    {
        var client = CreateClient();
        var email = await RegisterAsync(client);
        var unknown = $"nobody.{Guid.NewGuid():N}@example.com";
        var before = _factory.Emails.Sent.Count;

        var known = await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email));
        var missing = await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(unknown));

        Assert.Equal(known.StatusCode, missing.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await missing.Content.ReadAsStringAsync());
        Assert.Equal(before + 1, _factory.Emails.Sent.Count);
        Assert.DoesNotContain(_factory.Emails.Sent, message => message.To == unknown);
    }

    [Fact]
    public async Task ForgotPassword_ForGoogleOnlyAccount_ExplainsGoogleSignIn_AndIssuesNoToken()
    {
        var client = CreateClient();
        var email = $"google.{Guid.NewGuid():N}@example.com";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Add(new User { Email = email, Name = "Gia Oogle", GoogleSubject = Guid.NewGuid().ToString("N") });
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var message = _factory.Emails.LastTo(email);
        Assert.Contains("Google", message.TextBody);
        Assert.DoesNotContain("token=", message.TextBody);
        using var check = _factory.Services.CreateScope();
        var checkDb = check.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await checkDb.PasswordResetTokens.AnyAsync(token => token.User.Email == email));
    }

    [Fact]
    public async Task ResetPassword_WithValidToken_ChangesThePassword()
    {
        var client = CreateClient();
        var email = await RegisterAsync(client);
        var raw = await RequestResetAsync(client, email);

        var response = await client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(raw, NewPassword));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var oldLogin = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        var newLogin = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, NewPassword));
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_CannotBeUsedTwice()
    {
        var client = CreateClient();
        var email = await RegisterAsync(client);
        var raw = await RequestResetAsync(client, email);

        var first = await client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(raw, NewPassword));
        var second = await client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(raw, "Another1Passw0rd!"));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, NewPassword));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_WithExpiredToken_IsRejectedAndPasswordUnchanged()
    {
        var client = CreateClient();
        var email = await RegisterAsync(client);
        var raw = await RequestResetAsync(client, email);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var token = await db.PasswordResetTokens.SingleAsync(candidate => candidate.User.Email == email);
            token.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(raw, NewPassword));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("expired", await response.Content.ReadAsStringAsync());
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_WithUnknownToken_IsRejected()
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/reset-password",
            new ResetPasswordRequest("not-a-real-token", NewPassword));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_WithWeakPassword_IsRejected_AndTokenStaysUsable()
    {
        var client = CreateClient();
        var email = await RegisterAsync(client);
        var raw = await RequestResetAsync(client, email);

        var weak = await client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(raw, "weak"));
        var retry = await client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(raw, NewPassword));

        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    [Fact]
    public async Task ForgotPassword_AgainInvalidatesThePreviousLink()
    {
        var client = CreateClient();
        var email = await RegisterAsync(client);
        var first = await RequestResetAsync(client, email);
        var second = await RequestResetAsync(client, email);

        var oldAttempt = await client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(first, NewPassword));
        var newAttempt = await client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(second, NewPassword));

        Assert.NotEqual(first, second);
        Assert.Equal(HttpStatusCode.BadRequest, oldAttempt.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newAttempt.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_ClearsLockoutState()
    {
        var client = CreateClient();
        var email = await RegisterAsync(client);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.SingleAsync(candidate => candidate.Email == email);
            user.FailedLoginAttempts = 3;
            user.LockoutEnd = DateTime.UtcNow.AddMinutes(10);
            await db.SaveChangesAsync();
        }
        var raw = await RequestResetAsync(client, email);

        var response = await client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(raw, NewPassword));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var check = _factory.Services.CreateScope();
        var checkDb = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var updated = await checkDb.Users.AsNoTracking().SingleAsync(candidate => candidate.Email == email);
        Assert.Equal(0, updated.FailedLoginAttempts);
        Assert.Null(updated.LockoutEnd);
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, NewPassword));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_RevokesEveryRefreshToken()
    {
        var client = CreateClient();
        var email = await RegisterAsync(client);
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var raw = await RequestResetAsync(client, email);

        await client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(raw, NewPassword));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tokens = await db.RefreshTokens.Where(token => token.User.Email == email).ToListAsync();
        Assert.NotEmpty(tokens);
        Assert.All(tokens, token => Assert.NotNull(token.RevokedAt));
        Assert.All(tokens, token => Assert.Null(token.GraceExpiresAt));
        var refresh = await client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task ForgotPassword_IsRateLimited()
    {
        await using var factory = new RateLimitedAuthWebApplicationFactory();
        var client = factory.CreateClient();
        var request = new ForgotPasswordRequest($"nobody.{Guid.NewGuid():N}@example.com");

        var first = await client.PostAsJsonAsync("/api/auth/forgot-password", request);
        var second = await client.PostAsJsonAsync("/api/auth/forgot-password", request);
        var third = await client.PostAsJsonAsync("/api/auth/forgot-password", request);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    }

    [Fact]
    public async Task ForgotPassword_StillAnswers202_WhenMailServerIsDown()
    {
        await using var factory = new FailingEmailWebApplicationFactory();
        var client = factory.CreateClient();
        var email = $"jane.{Guid.NewGuid():N}@example.com";
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Jane", "Doe", email, Password));

        var response = await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
        });

    private static async Task<string> RegisterAsync(HttpClient client)
    {
        var email = $"reset.{Guid.NewGuid():N}@example.com";
        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest("Jane", "Doe", email, Password));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return email;
    }

    private async Task<string> RequestResetAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return CapturingEmailSender.TokenFrom(_factory.Emails.LastTo(email));
    }
}
