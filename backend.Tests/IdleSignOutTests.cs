using System.IdentityModel.Tokens.Jwt;
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
 * Test cases (the test factory uses a 60-minute access token and a 60-minute idle window):
 * 1. The access JWT carries the session id, equal to the refresh-token family.
 * 2. An authenticated request moves LastSeenAt forward when it is more than a minute old.
 * 3. Within a minute of the last bump, a request does not write again.
 * 4. A refresh after the idle window is refused, and the session is ended and its tokens revoked.
 * 5. A refresh just inside the idle window still works.
 * 6. A refresh token whose login has no session row is refused.
 * 7. /me returns idleExpiresAt = LastSeenAt + the idle window.
 * 8. An idle window shorter than the access-token lifetime stops the app at startup.
 * 9. A tab's idle check (X-Idle-Check header) does not count as activity.
 */
public sealed class IdleSignOutTests : IClassFixture<AuthWebApplicationFactory>
{
    private const string Password = "Password1!";
    private const int IdleMinutes = 60;

    private readonly AuthWebApplicationFactory _factory;

    public IdleSignOutTests(AuthWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AccessToken_CarriesSessionId()
    {
        var email = await SeedAsync(_factory);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));

        var access = new JwtSecurityTokenHandler().ReadJwtToken(CookieFrom(login, AuthCookie.AccessTokenName));
        var session = await SessionAsync(_factory, email);
        Assert.Equal(session.Id.ToString(), access.Claims.Single(claim => claim.Type == TokenProvider.SessionIdClaim).Value);
    }

    [Fact]
    public async Task Request_MovesLastSeenForward_WhenOlderThanAMinute()
    {
        var email = await SeedAsync(_factory);
        var client = await SignedInAsync(_factory, email);
        await SetLastSeenAsync(_factory, email, DateTime.UtcNow.AddMinutes(-5));

        var me = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.True((await SessionAsync(_factory, email)).LastSeenAt > DateTime.UtcNow.AddSeconds(-30));
    }

    [Fact]
    public async Task Request_WithinAMinute_DoesNotWriteAgain()
    {
        var email = await SeedAsync(_factory);
        var client = await SignedInAsync(_factory, email);
        var recent = DateTime.UtcNow.AddSeconds(-30);
        await SetLastSeenAsync(_factory, email, recent);

        await client.GetAsync("/api/auth/me");

        Assert.Equal(recent, (await SessionAsync(_factory, email)).LastSeenAt);
    }

    [Fact]
    public async Task Refresh_AfterIdleWindow_IsRefusedAndEndsTheSession()
    {
        var email = await SeedAsync(_factory);
        var client = await SignedInAsync(_factory, email);
        await SetLastSeenAsync(_factory, email, DateTime.UtcNow.AddMinutes(-IdleMinutes - 1));

        var refresh = await client.PostAsync("/api/auth/refresh", null);

        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        var session = await SessionAsync(_factory, email);
        Assert.NotNull(session.EndedAt);
        using var scope = _factory.Services.CreateScope();
        var live = await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .RefreshTokens.CountAsync(token => token.FamilyId == session.Id && token.RevokedAt == null);
        Assert.Equal(0, live);
    }

    [Fact]
    public async Task Refresh_InsideIdleWindow_StillWorks()
    {
        var email = await SeedAsync(_factory);
        var client = await SignedInAsync(_factory, email);
        await SetLastSeenAsync(_factory, email, DateTime.UtcNow.AddMinutes(-IdleMinutes + 1));

        var refresh = await client.PostAsync("/api/auth/refresh", null);

        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.Null((await SessionAsync(_factory, email)).EndedAt);
    }

    [Fact]
    public async Task Refresh_WithoutSessionRow_IsRefused()
    {
        var email = await SeedAsync(_factory);
        var client = await SignedInAsync(_factory, email);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.UserSessions.Remove(await db.UserSessions.SingleAsync(session => session.User.Email == email));
            await db.SaveChangesAsync();
        }

        var refresh = await client.PostAsync("/api/auth/refresh", null);

        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Me_ReturnsIdleExpiry()
    {
        var email = await SeedAsync(_factory);
        var client = await SignedInAsync(_factory, email);
        var lastSeen = DateTime.UtcNow.AddSeconds(-20);
        await SetLastSeenAsync(_factory, email, lastSeen);

        var me = await client.GetFromJsonAsync<MeResponse>("/api/auth/me");

        Assert.Equal(lastSeen.AddMinutes(IdleMinutes), me!.IdleExpiresAt!.Value, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task IdleCheck_DoesNotCountAsActivity()
    {
        var email = await SeedAsync(_factory);
        var client = await SignedInAsync(_factory, email);
        var fiveMinutesAgo = DateTime.UtcNow.AddMinutes(-5);
        await SetLastSeenAsync(_factory, email, fiveMinutesAgo);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Add("X-Idle-Check", "1");

        var me = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(fiveMinutesAgo, (await SessionAsync(_factory, email)).LastSeenAt);
    }

    [Fact]
    public async Task IdleWindowShorterThanAccessToken_StopsTheAppAtStartup()
    {
        await using var factory = new IdleConfigWebApplicationFactory(idleMinutes: 5);

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    private static string CookieFrom(HttpResponseMessage response, string name)
    {
        var header = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith($"{name}=", StringComparison.Ordinal));
        return header[(name.Length + 1)..header.IndexOf(';')];
    }

    private static async Task<UserSession> SessionAsync(AuthWebApplicationFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .UserSessions.AsNoTracking().SingleAsync(session => session.User.Email == email);
    }

    private static async Task SetLastSeenAsync(AuthWebApplicationFactory factory, string email, DateTime lastSeen)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.UserSessions.SingleAsync(session => session.User.Email == email)).LastSeenAt = lastSeen;
        await db.SaveChangesAsync();
    }

    /// <summary>Unverified, so a password login signs in without the code step.</summary>
    private static async Task<string> SeedAsync(AuthWebApplicationFactory factory)
    {
        var email = $"user.{Guid.NewGuid():N}@example.com";
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var user = new User { Email = email, Name = "Test User" };
        user.PasswordHash = hasher.HashPassword(user, Password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return email;
    }

    private static async Task<HttpClient> SignedInAsync(AuthWebApplicationFactory factory, string email)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return client;
    }
}

/// <summary>Factory with its own idle window, for startup validation.</summary>
public sealed class IdleConfigWebApplicationFactory(int idleMinutes) : AuthWebApplicationFactory
{
    protected override IReadOnlyDictionary<string, string?> ExtraConfig { get; } =
        new Dictionary<string, string?> { ["Jwt:IdleTimeoutMinutes"] = idleMinutes.ToString() };
}
