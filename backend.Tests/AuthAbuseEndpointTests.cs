using System.Net;
using System.Net.Http.Json;
using backend.Data;
using backend.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace backend.Tests;

/*
 * Test cases:
 * 1. Register is rate limited per IP.
 * 2. A password over the length cap is rejected on register and no user is created.
 * 3. An email over the length cap is rejected on register.
 * 4. A password over the length cap is rejected on login before any hashing.
 * 5. Login for an unknown email still performs a password verification (no timing shortcut).
 * 6. Unknown email, wrong password and locked account return the same status and body.
 * 7. A locked account stays locked and rejects the correct password.
 */
public sealed class AuthAbuseEndpointTests
{
    private const string Password = "Password1!";

    private static readonly string TooLongPassword = "Aa1!" + new string('x', 125);

    [Fact]
    public async Task Register_ExceedingIpRateLimit_ReturnsTooManyRequests()
    {
        await using var factory = new RateLimitedAuthWebApplicationFactory();
        var client = factory.CreateClient();

        HttpStatusCode last = default;
        for (var i = 0; i < 3; i++)
        {
            last = (await client.PostAsJsonAsync(
                "/api/auth/register",
                new RegisterRequest("Jane", "Doe", $"flood.{Guid.NewGuid():N}@example.com", Password))).StatusCode;
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }

    [Fact]
    public async Task Register_PasswordOverLengthCap_IsRejectedAndCreatesNoUser()
    {
        await using var factory = new AuthWebApplicationFactory();
        var email = $"long.{Guid.NewGuid():N}@example.com";

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/auth/register", new RegisterRequest("Jane", "Doe", email, TooLongPassword));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Users.AnyAsync(user => user.Email == email));
    }

    [Fact]
    public async Task Register_EmailOverLengthCap_IsRejected()
    {
        await using var factory = new AuthWebApplicationFactory();
        var email = new string('a', 250) + "@example.com";

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/auth/register", new RegisterRequest("Jane", "Doe", email, Password));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_PasswordOverLengthCap_IsRejectedBeforeHashing()
    {
        await using var factory = new CountingHasherWebApplicationFactory();

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new LoginRequest("someone@example.com", TooLongPassword));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, factory.Hasher.Verifications);
    }

    [Fact]
    public async Task Login_UnknownEmail_StillPerformsAPasswordVerification()
    {
        await using var factory = new CountingHasherWebApplicationFactory();

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new LoginRequest($"ghost.{Guid.NewGuid():N}@example.com", Password));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, factory.Hasher.Verifications);
    }

    [Fact]
    public async Task Login_UnknownWrongPasswordAndLocked_AreIndistinguishable()
    {
        await using var factory = new AuthWebApplicationFactory();
        var client = factory.CreateClient();
        var real = await RegisterAsync(client);
        var locked = await RegisterAsync(client);
        for (var i = 0; i < 5; i++)
        {
            await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(locked, "WrongPass1!"));
        }

        var unknown = await LoginAsync(client, $"ghost.{Guid.NewGuid():N}@example.com", "WrongPass1!");
        var wrong = await LoginAsync(client, real, "WrongPass1!");
        var lockedResult = await LoginAsync(client, locked, "WrongPass1!");

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.Status);
        Assert.Equal(unknown, wrong);
        Assert.Equal(unknown, lockedResult);
    }

    [Fact]
    public async Task Login_LockedAccount_StaysLockedAndRejectsCorrectPassword()
    {
        await using var factory = new AuthWebApplicationFactory();
        var client = factory.CreateClient();
        var email = await RegisterAsync(client);
        for (var i = 0; i < 5; i++)
        {
            await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "WrongPass1!"));
        }

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(candidate => candidate.Email == email);
        Assert.True(user.LockoutEnd > DateTime.UtcNow);
    }

    private static async Task<string> RegisterAsync(HttpClient client)
    {
        var email = $"abuse.{Guid.NewGuid():N}@example.com";
        var response = await client.PostAsJsonAsync(
            "/api/auth/register", new RegisterRequest("Jane", "Doe", email, Password));
        response.EnsureSuccessStatusCode();
        return email;
    }

    private static async Task<(HttpStatusCode Status, string Body)> LoginAsync(
        HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }
}
