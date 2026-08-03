using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using backend.DTOs;
using backend.Services;

namespace backend.Tests;

/*
 * Test cases:
 * 1. Valid refresh rotates the refresh token and returns a new access token.
 * 2. Reusing a just-rotated refresh token within the grace period still works.
 * 3. Refresh without a cookie returns unauthorized.
 * 4. Refresh after logout returns unauthorized.
 * 5. Refresh with an unknown token returns unauthorized.
 */
public sealed class RefreshEndpointTests : IClassFixture<AuthWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly AuthWebApplicationFactory _factory;

    public RefreshEndpointTests(AuthWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Refresh_WithValidCookie_RotatesRefreshAndReturnsNewAccess()
    {
        var client = CreateClient();
        var (_, refreshBefore) = await LoginAndGetTokensAsync(client);

        var refreshResponse = await client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);

        var body = await refreshResponse.Content.ReadFromJsonAsync<RefreshResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.True(body.ExpiresAt > DateTime.UtcNow);

        Assert.True(refreshResponse.Headers.TryGetValues("Set-Cookie", out var cookies));
        var setCookie = string.Join("\n", cookies);
        Assert.Contains($"{AuthCookie.AccessTokenName}=", setCookie);
        Assert.Contains($"{AuthCookie.RefreshTokenName}=", setCookie);

        var refreshAfter = GetSetCookieValue(cookies, AuthCookie.RefreshTokenName);
        Assert.False(string.IsNullOrWhiteSpace(refreshAfter));
        Assert.NotEqual(refreshBefore, refreshAfter);

        var meResponse = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
    }

    [Fact]
    public async Task Refresh_ReusingRotatedTokenWithinGrace_ReturnsNewAccess()
    {
        var client = CreateClient();
        var (_, originalRefresh) = await LoginAndGetTokensAsync(client);

        // Winning rotation (Tab A).
        var firstRefresh = await client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.OK, firstRefresh.StatusCode);

        // Losing twin request still carries the old refresh (Tab B).
        using var replay = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        replay.Headers.Add("Cookie", $"{AuthCookie.RefreshTokenName}={originalRefresh}");
        var graceResponse = await client.SendAsync(replay);

        Assert.Equal(HttpStatusCode.OK, graceResponse.StatusCode);
        Assert.True(graceResponse.Headers.TryGetValues("Set-Cookie", out var cookies));
        var setCookie = string.Join("\n", cookies);
        Assert.Contains($"{AuthCookie.AccessTokenName}=", setCookie);
    }

    [Fact]
    public async Task Refresh_WithoutCookie_ReturnsUnauthorized()
    {
        var client = CreateClient();
        var response = await client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_AfterLogout_ReturnsUnauthorized()
    {
        var client = CreateClient();
        await LoginAndGetTokensAsync(client);

        var logoutResponse = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        var refreshResponse = await client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithUnknownToken_ReturnsUnauthorized()
    {
        var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", $"{AuthCookie.RefreshTokenName}=not-a-real-refresh-token");
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
        });

    private static async Task<(string Access, string Refresh)> LoginAndGetTokensAsync(HttpClient client)
    {
        var email = $"refresh.{Guid.NewGuid():N}@example.com";
        const string password = "Password1!";

        var registerResponse = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest("Jane", "Doe", email, password));
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(email, password));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        Assert.True(loginResponse.Headers.TryGetValues("Set-Cookie", out var cookies));
        var access = GetSetCookieValue(cookies, AuthCookie.AccessTokenName);
        var refresh = GetSetCookieValue(cookies, AuthCookie.RefreshTokenName);
        Assert.False(string.IsNullOrWhiteSpace(access));
        Assert.False(string.IsNullOrWhiteSpace(refresh));
        return (access!, refresh!);
    }

    private static string? GetSetCookieValue(IEnumerable<string> setCookieHeaders, string name)
    {
        var prefix = name + "=";
        foreach (var header in setCookieHeaders)
        {
            // Each Set-Cookie header is one cookie; value ends at the first ';'
            if (!header.StartsWith(prefix, StringComparison.Ordinal))
            {
                // Some hosts fold multiple cookies; scan segments.
                var match = Regex.Match(header, $@"(?:^|,\s*){Regex.Escape(name)}=([^;]+)");
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }

                continue;
            }

            var end = header.IndexOf(';');
            return end < 0 ? header[prefix.Length..] : header[prefix.Length..end];
        }

        return null;
    }
}
