using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using backend.Data;
using backend.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace backend.Tests;

/*
 * Test cases:
 * 1. Registering emails a verification link to the new address.
 * 2. Only the token hash is stored, never the raw token.
 * 3. A valid token stamps EmailVerifiedAt.
 * 4. A token cannot be used twice.
 * 5. An expired token is rejected and the user stays unverified.
 * 6. An unknown token is rejected.
 * 7. Resend emails a fresh link and invalidates the previous one.
 * 8. Resend for an unknown address answers 202 and sends nothing.
 * 9. Resend for an already-verified address answers 202 and sends nothing.
 * 10. Resend is rate limited.
 * 11. An unverified user is blocked from a verified-only route until verified and refreshed.
 * 12. /me reports whether the email is verified.
 * 13. Registration still succeeds when the mail server is down.
 */
public sealed class VerifyEmailEndpointTests : IClassFixture<AuthWebApplicationFactory>
{
    private const string Password = "Password1!";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly AuthWebApplicationFactory _factory;

    public VerifyEmailEndpointTests(AuthWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Register_EmailsVerificationLinkToNewAddress()
    {
        var client = CreateClient();
        var email = await RegisterAsync(client);

        var message = _factory.Emails.LastTo(email);
        Assert.Contains("/verify-email?token=", message.TextBody);
        Assert.False(string.IsNullOrWhiteSpace(CapturingEmailSender.TokenFrom(message)));
    }

    [Fact]
    public async Task Register_StoresOnlyTheTokenHash()
    {
        var client = CreateClient();
        var email = await RegisterAsync(client);
        var raw = CapturingEmailSender.TokenFrom(_factory.Emails.LastTo(email));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(candidate => candidate.Email == email);
        var stored = await db.EmailVerificationTokens.SingleAsync(token => token.UserId == user.Id);

        Assert.NotEqual(raw, stored.TokenHash);
        Assert.DoesNotContain(raw, stored.TokenHash);
        Assert.True(stored.ExpiresAt > DateTime.UtcNow);
        Assert.Null(stored.UsedAt);
    }

    [Fact]
    public async Task VerifyEmail_WithValidToken_StampsUser()
    {
        var client = CreateClient();
        var email = await RegisterAsync(client);
        var raw = CapturingEmailSender.TokenFrom(_factory.Emails.LastTo(email));

        var response = await client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(raw));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull((await FindUserAsync(email)).EmailVerifiedAt);
    }

    [Fact]
    public async Task VerifyEmail_CannotBeUsedTwice()
    {
        var client = CreateClient();
        var email = await RegisterAsync(client);
        var raw = CapturingEmailSender.TokenFrom(_factory.Emails.LastTo(email));

        var first = await client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(raw));
        var second = await client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(raw));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task VerifyEmail_WithExpiredToken_IsRejectedAndUserStaysUnverified()
    {
        var client = CreateClient();
        var email = await RegisterAsync(client);
        var raw = CapturingEmailSender.TokenFrom(_factory.Emails.LastTo(email));

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.SingleAsync(candidate => candidate.Email == email);
            var token = await db.EmailVerificationTokens.SingleAsync(candidate => candidate.UserId == user.Id);
            token.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(raw));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("expired", await response.Content.ReadAsStringAsync());
        Assert.Null((await FindUserAsync(email)).EmailVerifiedAt);
    }

    [Fact]
    public async Task VerifyEmail_WithUnknownToken_IsRejected()
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/verify-email",
            new VerifyEmailRequest("not-a-real-token"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Resend_SendsFreshLink_AndInvalidatesThePreviousOne()
    {
        var client = CreateClient();
        var email = await RegisterAsync(client);
        var first = CapturingEmailSender.TokenFrom(_factory.Emails.LastTo(email));

        var resend = await client.PostAsJsonAsync("/api/auth/resend-verification", new ResendVerificationRequest(email));
        var second = CapturingEmailSender.TokenFrom(_factory.Emails.LastTo(email));

        Assert.Equal(HttpStatusCode.Accepted, resend.StatusCode);
        Assert.NotEqual(first, second);

        var oldAttempt = await client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(first));
        var newAttempt = await client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(second));
        Assert.Equal(HttpStatusCode.BadRequest, oldAttempt.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newAttempt.StatusCode);
    }

    [Fact]
    public async Task Resend_ForUnknownAddress_Returns202AndSendsNothing()
    {
        var client = CreateClient();
        var before = _factory.Emails.Sent.Count;

        var response = await client.PostAsJsonAsync(
            "/api/auth/resend-verification",
            new ResendVerificationRequest($"nobody.{Guid.NewGuid():N}@example.com"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(before, _factory.Emails.Sent.Count);
    }

    [Fact]
    public async Task Resend_ForVerifiedAddress_Returns202AndSendsNothing()
    {
        var client = CreateClient();
        var email = await RegisterAsync(client);
        await VerifyAsync(client, email);
        var before = _factory.Emails.Sent.Count;

        var response = await client.PostAsJsonAsync("/api/auth/resend-verification", new ResendVerificationRequest(email));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(before, _factory.Emails.Sent.Count);
    }

    [Fact]
    public async Task Resend_IsRateLimited()
    {
        await using var factory = new RateLimitedAuthWebApplicationFactory();
        var client = factory.CreateClient();
        var request = new ResendVerificationRequest($"nobody.{Guid.NewGuid():N}@example.com");

        var first = await client.PostAsJsonAsync("/api/auth/resend-verification", request);
        var second = await client.PostAsJsonAsync("/api/auth/resend-verification", request);
        var third = await client.PostAsJsonAsync("/api/auth/resend-verification", request);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    }

    [Fact]
    public async Task VerifiedOnlyRoute_BlocksUnverifiedUntilVerifiedAndRefreshed()
    {
        var client = CreateClient();
        var email = await RegisterAsync(client);
        await LoginAsync(client, email);

        var blocked = await client.GetAsync("/api/test/verified-only");
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);

        await VerifyAsync(client, email);
        var refresh = await client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);

        var allowed = await client.GetAsync("/api/test/verified-only");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task Me_ReportsEmailVerification()
    {
        var client = CreateClient();
        var email = await RegisterAsync(client);
        await LoginAsync(client, email);

        var before = await (await client.GetAsync("/api/auth/me")).Content.ReadFromJsonAsync<MeResponse>(JsonOptions);
        await VerifyAsync(client, email);
        var after = await (await client.GetAsync("/api/auth/me")).Content.ReadFromJsonAsync<MeResponse>(JsonOptions);

        Assert.False(before!.EmailVerified);
        Assert.True(after!.EmailVerified);
    }

    [Fact]
    public async Task Register_StillSucceeds_WhenMailServerIsDown()
    {
        await using var factory = new FailingEmailWebApplicationFactory();
        var client = factory.CreateClient();
        var email = $"jane.{Guid.NewGuid():N}@example.com";

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest("Jane", "Doe", email, Password));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.Users.AnyAsync(user => user.Email == email));
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
        });

    private static async Task<string> RegisterAsync(HttpClient client)
    {
        var email = $"verify.{Guid.NewGuid():N}@example.com";
        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest("Jane", "Doe", email, Password));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return email;
    }

    private static async Task LoginAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task VerifyAsync(HttpClient client, string email)
    {
        var raw = CapturingEmailSender.TokenFrom(_factory.Emails.LastTo(email));
        var response = await client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(raw));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<backend.Models.User> FindUserAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.AsNoTracking().SingleAsync(user => user.Email == email);
    }
}
