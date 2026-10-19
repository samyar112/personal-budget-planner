using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace backend.Tests;

/*
 * Test cases:
 * 1. Production refuses to start with AllowedHosts = "*".
 * 2. Production with a real host serves that host and rejects any other Host header.
 * 3. Production responses carry the security headers, including on error responses.
 * 4. HSTS is sent over https in Production.
 * 5. Development responses carry no CSP, so the Scalar UI keeps working.
 */
public sealed class HttpHardeningTests
{
    private const string Host = "api.example.com";

    [Fact]
    public void Production_WithWildcardAllowedHosts_FailsToStart()
    {
        using var factory = new ProductionWebApplicationFactory("*");

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    [Fact]
    public async Task Production_WithRealHost_RejectsOtherHostHeaders()
    {
        await using var factory = new ProductionWebApplicationFactory(Host);

        var allowed = await Send(factory, "https://api.example.com/health");
        var spoofed = await Send(factory, "https://evil.example.net/health");

        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, spoofed.StatusCode);
    }

    [Fact]
    public async Task Production_Responses_CarrySecurityHeaders()
    {
        await using var factory = new ProductionWebApplicationFactory(Host);

        foreach (var url in new[] { "https://api.example.com/health", "https://api.example.com/api/auth/me" })
        {
            var response = await Send(factory, url);

            Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
            Assert.Equal("DENY", Header(response, "X-Frame-Options"));
            Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
            Assert.Equal("default-src 'none'; frame-ancestors 'none'", Header(response, "Content-Security-Policy"));
        }
    }

    [Fact]
    public async Task Production_OverHttps_SendsHsts()
    {
        await using var factory = new ProductionWebApplicationFactory(Host);

        var response = await Send(factory, "https://api.example.com/health");

        Assert.Contains("max-age=", Header(response, "Strict-Transport-Security"));
    }

    [Fact]
    public async Task Development_Responses_HaveNoContentSecurityPolicy()
    {
        await using var factory = new AuthWebApplicationFactory();

        var response = await factory.CreateClient().GetAsync("/health");

        Assert.False(response.Headers.Contains("Content-Security-Policy"));
        Assert.False(response.Headers.Contains("X-Frame-Options"));
    }

    private static async Task<HttpResponseMessage> Send(AuthWebApplicationFactory factory, string url)
    {
        var uri = new Uri(url);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"{uri.Scheme}://{uri.Host}"),
        });
        return await client.GetAsync(uri.PathAndQuery);
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;
}
