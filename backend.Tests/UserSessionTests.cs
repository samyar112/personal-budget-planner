using System.Net;
using System.Net.Http.Json;
using backend.Data;
using backend.DTOs;
using backend.Models;
using backend.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace backend.Tests;

/*
 * Test cases:
 * 1. A password login (unverified account, no code step) starts one session keyed by the refresh-token family.
 * 2. A password-and-code login records EmailCode; a Google sign-in records Google.
 * 3. A refresh moves LastSeenAt forward and keeps the same session.
 * 4. Logout ends the session.
 * 5. Replaying a rotated token after grace ends the session.
 * 6. Deactivation ends every session of that user, and only that user.
 * 7. A password reset ends every session of that user.
 * 8. GET /api/admin/sessions: 401 without a session, 403 for a regular user.
 * 9. active=true returns only sessions not ended and seen within the access-token lifetime.
 * 10. The response carries no token hash, and sessions come most recently seen first, paged.
 */
public sealed class UserSessionTests : IClassFixture<AuthWebApplicationFactory>
{
    private const string Password = "Password1!";

    private readonly AuthWebApplicationFactory _factory;

    public UserSessionTests(AuthWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PasswordLogin_StartsSessionKeyedByRefreshFamily()
    {
        var (userId, email) = await SeedAsync(_factory, verified: false);

        await SignedInAsync(_factory, email);

        var session = Assert.Single(await SessionsFor(_factory, userId));
        var family = (await TokensFor(_factory, userId)).Single().FamilyId;
        Assert.Equal(family, session.Id);
        Assert.Equal(LoginMethod.Password, session.LoginMethod);
        Assert.Null(session.EndedAt);
        Assert.True(session.StartedAt > DateTime.UtcNow.AddMinutes(-1));
        Assert.Equal(session.StartedAt, session.LastSeenAt);
    }

    [Fact]
    public async Task CodeAndGoogleLogins_RecordTheirMethod()
    {
        var (codeUserId, codeEmail) = await SeedAsync(_factory);
        await SignedInAsync(_factory, codeEmail);

        var identity = new GoogleIdentity($"sub-{Guid.NewGuid():N}", $"google.{Guid.NewGuid():N}@example.com", "Gina");
        var google = await _factory.CreateClient().PostAsJsonAsync(
            "/api/auth/google", new GoogleLoginRequest(_factory.Google.AddToken(identity)));
        Assert.Equal(HttpStatusCode.OK, google.StatusCode);
        var googleUserId = await UserIdFor(_factory, identity.Email);

        Assert.Equal(LoginMethod.EmailCode, Assert.Single(await SessionsFor(_factory, codeUserId)).LoginMethod);
        Assert.Equal(LoginMethod.Google, Assert.Single(await SessionsFor(_factory, googleUserId)).LoginMethod);
    }

    [Fact]
    public async Task Refresh_MovesLastSeenForward()
    {
        var (userId, email) = await SeedAsync(_factory, verified: false);
        var client = await SignedInAsync(_factory, email);
        var halfHourAgo = DateTime.UtcNow.AddMinutes(-30); // inside the 60-minute test idle window
        await UpdateSessionsAsync(_factory, userId, session => session.LastSeenAt = halfHourAgo);

        var refresh = await client.PostAsync("/api/auth/refresh", null);

        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var session = Assert.Single(await SessionsFor(_factory, userId));
        Assert.True(session.LastSeenAt > DateTime.UtcNow.AddMinutes(-1));
        Assert.Null(session.EndedAt);
    }

    [Fact]
    public async Task Logout_EndsTheSession()
    {
        var (userId, email) = await SeedAsync(_factory, verified: false);
        var client = await SignedInAsync(_factory, email);

        await client.PostAsync("/api/auth/logout", null);

        Assert.NotNull(Assert.Single(await SessionsFor(_factory, userId)).EndedAt);
    }

    [Fact]
    public async Task ReplayAfterGrace_EndsTheSession()
    {
        var (userId, email) = await SeedAsync(_factory, verified: false);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        var original = RefreshCookieFrom(login);
        Assert.Equal(HttpStatusCode.OK, (await RefreshWithAsync(client, original)).StatusCode);
        await UpdateTokensAsync(_factory, userId, token => token.GraceExpiresAt = DateTime.UtcNow.AddSeconds(-1));

        var replay = await RefreshWithAsync(client, original);

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.NotNull(Assert.Single(await SessionsFor(_factory, userId)).EndedAt);
    }

    [Fact]
    public async Task Deactivation_EndsOnlyThatUsersSessions()
    {
        var (_, adminEmail) = await SeedAsync(_factory, role: UserRole.Admin);
        var (targetId, targetEmail) = await SeedAsync(_factory, verified: false);
        var (bystanderId, bystanderEmail) = await SeedAsync(_factory, verified: false);
        await SignedInAsync(_factory, targetEmail);
        await SignedInAsync(_factory, targetEmail);
        await SignedInAsync(_factory, bystanderEmail);
        var admin = await SignedInAsync(_factory, adminEmail);

        var response = await admin.PostAsync($"/api/admin/users/{targetId}/deactivate", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var targetSessions = await SessionsFor(_factory, targetId);
        Assert.Equal(2, targetSessions.Count);
        Assert.All(targetSessions, session => Assert.NotNull(session.EndedAt));
        Assert.Null(Assert.Single(await SessionsFor(_factory, bystanderId)).EndedAt);
    }

    [Fact]
    public async Task PasswordReset_EndsEverySession()
    {
        var (userId, email) = await SeedAsync(_factory, verified: false);
        await SignedInAsync(_factory, email);
        var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email));
        var token = CapturingEmailSender.TokenFrom(_factory.Emails.LastTo(email));

        var reset = await client.PostAsJsonAsync(
            "/api/auth/reset-password", new ResetPasswordRequest(token, "NewPassword1!"));

        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.NotNull(Assert.Single(await SessionsFor(_factory, userId)).EndedAt);
    }

    [Fact]
    public async Task ListSessions_RefusesAnonymousAndRegularUsers()
    {
        var (_, email) = await SeedAsync(_factory);
        var user = await SignedInAsync(_factory, email);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _factory.CreateClient().GetAsync("/api/admin/sessions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/admin/sessions")).StatusCode);
    }

    [Fact]
    public async Task ListSessions_ActiveFilterAndOrderingAndNoSecrets()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (adminId, adminEmail) = await SeedAsync(factory, role: UserRole.Admin);
        var (userId, userEmail) = await SeedAsync(factory);
        var now = DateTime.UtcNow;
        var active = Session(userId, now.AddMinutes(-5));
        var stale = Session(userId, now.AddHours(-3));
        var ended = Session(userId, now.AddMinutes(-2));
        ended.EndedAt = now.AddMinutes(-1);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.UserSessions.AddRange(stale, ended, active);
            await db.SaveChangesAsync();
        }

        var admin = await SignedInAsync(factory, adminEmail);
        var adminSession = Assert.Single(await SessionsFor(factory, adminId)).Id;

        var activeResponse = await admin.GetAsync("/api/admin/sessions?active=true");
        var body = await activeResponse.Content.ReadAsStringAsync();
        var activeList = await activeResponse.Content.ReadFromJsonAsync<UserSessionListResponse>();
        var firstPage = await admin.GetFromJsonAsync<UserSessionListResponse>("/api/admin/sessions?pageSize=2");
        var secondPage = await admin.GetFromJsonAsync<UserSessionListResponse>("/api/admin/sessions?page=2&pageSize=2");

        Assert.Equal(HttpStatusCode.OK, activeResponse.StatusCode);
        Assert.DoesNotContain("hash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal([adminSession, active.Id], activeList!.Items.Select(item => item.Id));
        Assert.All(activeList.Items, item => Assert.True(item.IsActive));
        var row = activeList.Items.Single(item => item.Id == active.Id);
        Assert.Equal(userEmail, row.Email);
        Assert.Equal("EmailCode", row.LoginMethod);

        Assert.Equal(4, firstPage!.Total);
        Assert.Equal([adminSession, ended.Id], firstPage.Items.Select(item => item.Id));
        Assert.Equal([active.Id, stale.Id], secondPage!.Items.Select(item => item.Id));
        Assert.False(firstPage.Items.Single(item => item.Id == ended.Id).IsActive);
    }

    private static UserSession Session(Guid userId, DateTime lastSeen) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        LoginMethod = LoginMethod.EmailCode,
        StartedAt = lastSeen.AddMinutes(-30),
        LastSeenAt = lastSeen,
    };

    private static string RefreshCookieFrom(HttpResponseMessage response)
    {
        var header = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith($"{AuthCookie.RefreshTokenName}=", StringComparison.Ordinal));
        return header[(AuthCookie.RefreshTokenName.Length + 1)..header.IndexOf(';')];
    }

    private static Task<HttpResponseMessage> RefreshWithAsync(HttpClient client, string refreshToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", $"{AuthCookie.RefreshTokenName}={refreshToken}");
        return client.SendAsync(request);
    }

    private static async Task<List<UserSession>> SessionsFor(AuthWebApplicationFactory factory, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .UserSessions.AsNoTracking().Where(session => session.UserId == userId).ToListAsync();
    }

    private static async Task<List<RefreshToken>> TokensFor(AuthWebApplicationFactory factory, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .RefreshTokens.AsNoTracking().Where(token => token.UserId == userId).ToListAsync();
    }

    private static async Task<Guid> UserIdFor(AuthWebApplicationFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Users.Where(user => user.Email == email).Select(user => user.Id).SingleAsync();
    }

    private static async Task UpdateSessionsAsync(AuthWebApplicationFactory factory, Guid userId, Action<UserSession> change)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var session in db.UserSessions.Where(session => session.UserId == userId))
        {
            change(session);
        }

        await db.SaveChangesAsync();
    }

    private static async Task UpdateTokensAsync(AuthWebApplicationFactory factory, Guid userId, Action<RefreshToken> change)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var token in db.RefreshTokens.Where(token => token.UserId == userId && token.RevokedAt != null))
        {
            change(token);
        }

        await db.SaveChangesAsync();
    }

    private static async Task<(Guid Id, string Email)> SeedAsync(
        AuthWebApplicationFactory factory, UserRole role = UserRole.User, bool verified = true)
    {
        var email = $"user.{Guid.NewGuid():N}@example.com";
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var user = new User
        {
            Email = email,
            Name = "Test User",
            Role = role,
            EmailVerifiedAt = verified ? DateTime.UtcNow : null,
        };
        user.PasswordHash = hasher.HashPassword(user, Password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (user.Id, email);
    }

    /// <summary>Password login, plus the emailed code when the account is verified.</summary>
    private static async Task<HttpClient> SignedInAsync(AuthWebApplicationFactory factory, string email)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        if (login.StatusCode == HttpStatusCode.Accepted)
        {
            var challenge = await login.Content.ReadFromJsonAsync<LoginChallengeResponse>();
            var code = CapturingEmailSender.CodeFrom(factory.Emails.LastTo(email));
            login = await client.PostAsJsonAsync(
                "/api/auth/login/verify-code", new VerifyLoginCodeRequest(challenge!.ChallengeId, code));
        }

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return client;
    }
}
