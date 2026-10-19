using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using backend.Data;
using backend.DTOs;
using backend.Models;
using backend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace backend.Tests;

/*
 * Test cases:
 * 1. Replaying a rotated refresh token after its grace window revokes the whole family, including the live token.
 * 2. Replaying a logged-out token does not touch the user's other sessions.
 * 3. Two parallel refreshes leave exactly one live refresh token (no fork).
 * 4. A login that loses a race superseding the previous challenge still succeeds.
 * 5. Login codes are not keyed with the raw JWT signing key.
 */
public sealed class SessionHardeningTests
{
    private const string Password = "Password1!";
    private const string JwtKey = "test-jwt-signing-key-at-least-32-chars!!";

    [Fact]
    public async Task Refresh_ReplayAfterGrace_RevokesTheWholeFamily()
    {
        await using var factory = new AuthWebApplicationFactory();
        var client = NewClient(factory);
        var email = await RegisterAsync(client);
        var original = await LoginAsync(client, email);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/auth/refresh", null)).StatusCode);
        await ExpireGraceAsync(factory, email);

        var replay = await RefreshWithAsync(Attacker(factory), original);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        // The thief's replay also burns the token the real user now holds.
        var legitimate = await client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, legitimate.StatusCode);
    }

    [Fact]
    public async Task Refresh_ReplayOfLoggedOutToken_LeavesOtherSessionsAlone()
    {
        await using var factory = new AuthWebApplicationFactory();
        var email = await RegisterAsync(NewClient(factory));
        var laptop = NewClient(factory);
        var phone = NewClient(factory);
        var laptopToken = await LoginAsync(laptop, email);
        await LoginAsync(phone, email);

        await laptop.PostAsync("/api/auth/logout", null);
        var stale = await RefreshWithAsync(Attacker(factory), laptopToken);

        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await phone.PostAsync("/api/auth/refresh", null)).StatusCode);
    }

    [Fact]
    public async Task Refresh_TwoParallelRotations_LeaveExactlyOneLiveToken()
    {
        await using var factory = new RaceWebApplicationFactory();
        var client = NewClient(factory);
        var email = await RegisterAsync(client);
        await LoginAsync(client, email);

        // The twin request wins the rotation while this one is mid-save.
        factory.Injector.Trigger = context => context.ChangeTracker.Entries<RefreshToken>().Any(entry =>
            entry.State == EntityState.Modified && entry.Property(token => token.RevokedAt).IsModified);
        factory.Injector.Race = async context =>
        {
            var id = context.ChangeTracker.Entries<RefreshToken>()
                .First(entry => entry.State == EntityState.Modified).Entity.Id;
            using var scope = factory.Services.CreateScope();
            var other = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var current = await other.RefreshTokens.SingleAsync(token => token.Id == id);
            var winner = new RefreshToken
            {
                UserId = current.UserId,
                FamilyId = current.FamilyId,
                TokenHash = Guid.NewGuid().ToString("N"),
                ExpiresAt = DateTime.UtcNow.AddDays(7),
            };
            other.RefreshTokens.Add(winner);
            current.RevokedAt = DateTime.UtcNow;
            current.GraceExpiresAt = DateTime.UtcNow.AddSeconds(20);
            current.ReplacedByTokenId = winner.Id;
            await other.SaveChangesAsync();
        };

        var response = await client.PostAsync("/api/auth/refresh", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var check = factory.Services.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(candidate => candidate.Email == email);
        Assert.Equal(1, await db.RefreshTokens.CountAsync(token => token.UserId == user.Id && token.RevokedAt == null));
    }

    [Fact]
    public async Task Login_LosingTheRaceToSupersedeAChallenge_StillSucceeds()
    {
        await using var factory = new RaceWebApplicationFactory();
        var client = NewClient(factory);
        var email = await RegisterAsync(client);
        await VerifyAsync(factory, email);
        Assert.Equal(HttpStatusCode.Accepted, (await PostLoginAsync(client, email)).StatusCode);

        factory.Injector.Trigger = context => context.ChangeTracker.Entries<LoginChallenge>().Any(entry =>
            entry.State == EntityState.Modified && entry.Property(challenge => challenge.UsedAt).IsModified);
        factory.Injector.Race = async context =>
        {
            var id = context.ChangeTracker.Entries<LoginChallenge>()
                .First(entry => entry.State == EntityState.Modified).Entity.Id;
            using var scope = factory.Services.CreateScope();
            var other = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await other.LoginChallenges.SingleAsync(challenge => challenge.Id == id)).UsedAt = DateTime.UtcNow;
            await other.SaveChangesAsync();
        };

        var response = await PostLoginAsync(client, email);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task LoginCode_IsNotKeyedWithTheRawJwtSigningKey()
    {
        await using var factory = new AuthWebApplicationFactory();
        var client = NewClient(factory);
        var email = await RegisterAsync(client);
        await VerifyAsync(factory, email);
        var response = await PostLoginAsync(client, email);
        var challenge = await response.Content.ReadFromJsonAsync<LoginChallengeResponse>();
        var code = CapturingEmailSender.CodeFrom(factory.Emails.LastTo(email));

        using var scope = factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .LoginChallenges.SingleAsync(row => row.Id == challenge!.ChallengeId);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(JwtKey));
        var legacy = Convert.ToBase64String(
            hmac.ComputeHash(Encoding.UTF8.GetBytes($"{challenge!.ChallengeId:N}:{code}")));

        Assert.NotEqual(legacy, stored.CodeHash);
    }

    private static HttpClient NewClient(AuthWebApplicationFactory factory) =>
        factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
        });

    /// <summary>A client with no cookie jar, so a replay carries only the token it is given.</summary>
    private static HttpClient Attacker(AuthWebApplicationFactory factory) =>
        factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
        });

    private static async Task<string> RegisterAsync(HttpClient client)
    {
        var email = $"session.{Guid.NewGuid():N}@example.com";
        (await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Jane", "Doe", email, Password)))
            .EnsureSuccessStatusCode();
        return email;
    }

    private static Task<HttpResponseMessage> PostLoginAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));

    /// <summary>Logs in an unverified user and returns the raw refresh token from the cookie.</summary>
    private static async Task<string> LoginAsync(HttpClient client, string email)
    {
        var response = await PostLoginAsync(client, email);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Regex.Match(
            string.Join("\n", response.Headers.GetValues("Set-Cookie")),
            $"{AuthCookie.RefreshTokenName}=([^;]+)").Groups[1].Value;
    }

    private static Task<HttpResponseMessage> RefreshWithAsync(HttpClient client, string refreshToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", $"{AuthCookie.RefreshTokenName}={refreshToken}");
        return client.SendAsync(request);
    }

    private static async Task ExpireGraceAsync(AuthWebApplicationFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(candidate => candidate.Email == email);
        foreach (var token in db.RefreshTokens.Where(row => row.UserId == user.Id && row.RevokedAt != null))
        {
            token.GraceExpiresAt = DateTime.UtcNow.AddSeconds(-1);
        }

        await db.SaveChangesAsync();
    }

    private static async Task VerifyAsync(AuthWebApplicationFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Users.SingleAsync(user => user.Email == email)).EmailVerifiedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }
}
