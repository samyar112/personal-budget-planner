using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using backend.Data;
using backend.DTOs;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using backend.Models;

namespace backend.Tests;

public sealed class RegisterEndpointTests : IClassFixture<AuthWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly AuthWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public RegisterEndpointTests(AuthWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Register_WithValidPayload_ReturnsCreatedAndHashesPassword()
    {
        var email = $"jane.{Guid.NewGuid():N}@example.com";
        var payload = new RegisterRequest("Jane", "Doe", email, "Password1!");

        var response = await _client.PostAsJsonAsync("/api/auth/register", payload);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<RegisterResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal(email.ToLowerInvariant(), body.Email);
        Assert.Equal("Jane Doe", body.Name);
        Assert.NotEqual(Guid.Empty, body.Id);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(u => u.Email == email.ToLowerInvariant());

        Assert.False(string.IsNullOrWhiteSpace(user.PasswordHash));
        Assert.NotEqual(payload.Password, user.PasswordHash!);

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var verify = hasher.VerifyHashedPassword(user, user.PasswordHash!, payload.Password);
        Assert.Equal(PasswordVerificationResult.Success, verify);
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_ReturnsConflict()
    {
        var email = $"dup.{Guid.NewGuid():N}@example.com";
        var payload = new RegisterRequest("Jane", "Doe", email, "Password1!");

        var first = await _client.PostAsJsonAsync("/api/auth/register", payload);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await _client.PostAsJsonAsync("/api/auth/register", payload);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Register_WithWeakPassword_ReturnsBadRequest()
    {
        var payload = new RegisterRequest(
            "Jane",
            "Doe",
            $"weak.{Guid.NewGuid():N}@example.com",
            "password");

        var response = await _client.PostAsJsonAsync("/api/auth/register", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_WithInvalidName_ReturnsBadRequest()
    {
        var payload = new RegisterRequest(
            "Jane123",
            "Doe",
            $"name.{Guid.NewGuid():N}@example.com",
            "Password1!");

        var response = await _client.PostAsJsonAsync("/api/auth/register", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
