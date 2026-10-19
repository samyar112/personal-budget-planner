using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using backend.DTOs;
using backend.Services;

namespace backend.Tests;

/*
 * Test cases:
 * 1. A client that is not a trusted proxy cannot pick its own rate-limit bucket with X-Forwarded-For.
 * 2. A trusted proxy's X-Forwarded-For is honoured, so real clients behind it are limited separately.
 * 3. A malformed Proxy:KnownNetworks value stops the app at startup.
 * 4. Production auth cookies are SameSite=Lax, HttpOnly and Secure.
 */
public sealed class ProxyAndCookieTests
{
    [Fact]
    public async Task Login_UntrustedPeerCannotSpoofItsIpWithForwardedFor()
    {
        await using var factory = new UntrustedPeerWebApplicationFactory();
        var client = factory.CreateClient();

        HttpStatusCode last = default;
        for (var i = 0; i < 4; i++)
        {
            last = (await PostLoginAsync(client, forwardedFor: $"198.51.100.{i + 1}")).StatusCode;
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }

    [Fact]
    public async Task Login_TrustedProxyForwardedForGivesEachClientItsOwnBucket()
    {
        await using var factory = new TrustedProxyWebApplicationFactory();
        var client = factory.CreateClient();

        HttpStatusCode last = default;
        for (var i = 0; i < 4; i++)
        {
            last = (await PostLoginAsync(client, forwardedFor: $"198.51.100.{i + 1}")).StatusCode;
        }

        Assert.NotEqual(HttpStatusCode.TooManyRequests, last);
    }

    [Fact]
    public void Startup_WithMalformedProxyNetwork_Fails()
    {
        using var factory = new BadProxyRangeWebApplicationFactory();

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    [Fact]
    public void AuthCookies_InProduction_AreLaxHttpOnlyAndSecure()
    {
        foreach (var options in new[]
                 {
                     AuthCookie.CreateAccess(isDevelopment: false),
                     AuthCookie.CreateRefresh(isDevelopment: false),
                 })
        {
            Assert.Equal(SameSiteMode.Lax, options.SameSite);
            Assert.True(options.HttpOnly);
            Assert.True(options.Secure);
        }
    }

    private static Task<HttpResponseMessage> PostLoginAsync(HttpClient client, string forwardedFor)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest("nobody@example.com", "WrongPass1!")),
        };
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        return client.SendAsync(request);
    }
}
