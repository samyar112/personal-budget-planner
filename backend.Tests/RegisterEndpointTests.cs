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

/*
 * Test cases:
 * 1. Valid registration creates the user and hashes the password.
 * 2. Duplicate email looks exactly like a success and creates nothing.
 * 2b. A duplicate gets an 'account already exists' notice, not a verification link.
 * 2c. A duplicate still does the password hashing work, so timing does not give it away.
 * 3. Weak password returns bad request.
 * 4. Invalid name returns bad request.
 */
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
    public async Task Register_WithDuplicateEmail_LooksLikeSuccessAndCreatesNothing()
    {
        var email = $"dup.{Guid.NewGuid():N}@example.com";
        var payload = new RegisterRequest("Jane", "Doe", email, "Password1!");

        var first = await _client.PostAsJsonAsync("/api/auth/register", payload);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        string originalHash;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            originalHash = (await db.Users.SingleAsync(u => u.Email == email)).PasswordHash!;
        }

        var second = await _client.PostAsJsonAsync(
            "/api/auth/register", payload with { FirstName = "Mallory", Password = "Different1!" });

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var body = await second.Content.ReadFromJsonAsync<RegisterResponse>(JsonOptions);
        Assert.Equal(email, body!.Email);
        using var check = _factory.Services.CreateScope();
        var checkDb = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await checkDb.Users.SingleAsync(u => u.Email == email);
        Assert.Equal("Jane Doe", user.Name);
        Assert.Equal(originalHash, user.PasswordHash);
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_SendsAnAccountExistsNoticeNotAVerificationLink()
    {
        var email = $"notice.{Guid.NewGuid():N}@example.com";
        var payload = new RegisterRequest("Jane", "Doe", email, "Password1!");
        await _client.PostAsJsonAsync("/api/auth/register", payload);
        var sentBefore = _factory.Emails.Sent.Count(message => message.To == email);

        await _client.PostAsJsonAsync("/api/auth/register", payload);

        var sent = _factory.Emails.Sent.Where(message => message.To == email).ToList();
        Assert.Equal(sentBefore + 1, sent.Count);
        Assert.Contains("already", sent.Last().Subject, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token=", sent.Last().TextBody);
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_StillHashesThePassword()
    {
        await using var factory = new CountingHasherWebApplicationFactory();
        var client = factory.CreateClient();
        var payload = new RegisterRequest("Jane", "Doe", $"hash.{Guid.NewGuid():N}@example.com", "Password1!");

        await client.PostAsJsonAsync("/api/auth/register", payload);
        var afterFirst = factory.Hasher.Hashes;
        await client.PostAsJsonAsync("/api/auth/register", payload);

        Assert.Equal(1, afterFirst);
        Assert.Equal(2, factory.Hasher.Hashes);
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
