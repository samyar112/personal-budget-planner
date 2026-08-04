using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using backend.DTOs;
using backend.Services;

namespace backend.Tests;

/*
 * Test cases:
 * 1. Valid login sets HttpOnly access and refresh cookies.
 * 2. Wrong password returns unauthorized.
 * 3. Unknown email returns unauthorized.
 * 4. /me without a cookie returns unauthorized.
 * 5. /me with a login cookie returns the current user.
 * 6. Logout clears cookies and /me becomes unauthorized.
 */
public sealed class LoginEndpointTests : IClassFixture<AuthWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly AuthWebApplicationFactory _factory;

    public LoginEndpointTests(AuthWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_WithValidCredentials_SetsHttpOnlyCookie()
    {
        var email = $"login.{Guid.NewGuid():N}@example.com";
        const string password = "Password1!";
        var client = CreateClient();
        await RegisterAsync(client, email, password);

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(email, password));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal(email.ToLowerInvariant(), body.Email);
        Assert.Equal("Jane Doe", body.Name);
        Assert.True(body.ExpiresAt > DateTime.UtcNow);

        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies));
        var setCookie = string.Join("\n", cookies);
        Assert.Contains($"{AuthCookie.AccessTokenName}=", setCookie);
        Assert.Contains($"{AuthCookie.RefreshTokenName}=", setCookie);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        // Development tests: Secure flag should be off.
        Assert.DoesNotContain("secure", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_WithInvalidPassword_ReturnsUnauthorized()
    {
        var email = $"badpass.{Guid.NewGuid():N}@example.com";
        var client = CreateClient();
        await RegisterAsync(client, email, "Password1!");

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(email, "WrongPass1!"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithUnknownEmail_ReturnsUnauthorized()
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest($"missing.{Guid.NewGuid():N}@example.com", "Password1!"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_WithoutCookie_ReturnsUnauthorized()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_WithLoginCookie_ReturnsCurrentUser()
    {
        var email = $"me.{Guid.NewGuid():N}@example.com";
        const string password = "Password1!";
        var client = CreateClient();
        await RegisterAsync(client, email, password);

        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(email, password));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var meResponse = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);

        var me = await meResponse.Content.ReadFromJsonAsync<MeResponse>(JsonOptions);
        Assert.NotNull(me);
        Assert.Equal(email.ToLowerInvariant(), me.Email);
        Assert.Equal("Jane Doe", me.Name);
        Assert.NotEqual(Guid.Empty, me.Id);
    }

    [Fact]
    public async Task Logout_ClearsCookie_AndMeBecomesUnauthorized()
    {
        var email = $"logout.{Guid.NewGuid():N}@example.com";
        const string password = "Password1!";
        var client = CreateClient();
        await RegisterAsync(client, email, password);

        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(email, password));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var logoutResponse = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        Assert.True(logoutResponse.Headers.TryGetValues("Set-Cookie", out var cookies));
        var setCookie = string.Join(",", cookies);
        Assert.Contains($"{AuthCookie.AccessTokenName}=", setCookie);

        // Expired / cleared cookie should not authenticate.
        var meResponse = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meResponse.StatusCode);
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
        });

    private static async Task RegisterAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest("Jane", "Doe", email, password));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
