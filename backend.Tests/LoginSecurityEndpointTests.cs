using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using backend.Data;
using backend.DTOs;
using Microsoft.Extensions.DependencyInjection;

namespace backend.Tests;

/*
 * Test cases:
 * 1. More login attempts than the IP limit returns 429.
 * 2. Five failed passwords lock the account and notify the user (423).
 * 3. After the lockout window expires, a correct password can sign in again.
 */
public sealed class LoginSecurityEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public async Task Login_ExceedingIpRateLimit_ReturnsTooManyRequests()
    {
        await using var factory = new RateLimitedAuthWebApplicationFactory();
        var client = factory.CreateClient();

        HttpResponseMessage? lastResponse = null;
        for (var i = 0; i < 4; i++)
        {
            lastResponse = await client.PostAsJsonAsync(
                "/api/auth/login",
                new LoginRequest($"nouser.{i}@example.com", "WrongPass1!"));
        }

        Assert.NotNull(lastResponse);
        Assert.Equal(HttpStatusCode.TooManyRequests, lastResponse.StatusCode);

        var body = await lastResponse.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions);
        Assert.NotNull(body);
        Assert.Contains("Too many login attempts", body.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_FiveFailedAttempts_LocksAccountAndNotifiesUser()
    {
        await using var factory = new AuthWebApplicationFactory();
        var client = factory.CreateClient();
        var email = $"lock.{Guid.NewGuid():N}@example.com";
        const string password = "Password1!";

        var registerResponse = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest("Jane", "Doe", email, password));
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        HttpResponseMessage? lastFail = null;
        for (var i = 0; i < 5; i++)
        {
            lastFail = await client.PostAsJsonAsync(
                "/api/auth/login",
                new LoginRequest(email, "WrongPass1!"));
        }

        Assert.NotNull(lastFail);
        Assert.Equal(HttpStatusCode.Locked, lastFail.StatusCode);

        var lockBody = await lastFail.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions);
        Assert.NotNull(lockBody);
        Assert.Contains("locked", lockBody.Message, StringComparison.OrdinalIgnoreCase);

        // Correct password still rejected while locked.
        var whileLocked = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(email, password));
        Assert.Equal(HttpStatusCode.Locked, whileLocked.StatusCode);
    }

    [Fact]
    public async Task Login_AfterLockoutExpires_AllowsSuccessfulLogin()
    {
        await using var factory = new AuthWebApplicationFactory();
        var client = factory.CreateClient();
        var email = $"unlock.{Guid.NewGuid():N}@example.com";
        const string password = "Password1!";

        var registerResponse = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest("Jane", "Doe", email, password));
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        for (var i = 0; i < 5; i++)
        {
            await client.PostAsJsonAsync(
                "/api/auth/login",
                new LoginRequest(email, "WrongPass1!"));
        }

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = Assert.Single(db.Users.Where(u => u.Email == email.ToLowerInvariant()).ToList());
            user.LockoutEnd = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        var success = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(email, password));
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
    }

    private sealed record ErrorBody(string Message);
}
