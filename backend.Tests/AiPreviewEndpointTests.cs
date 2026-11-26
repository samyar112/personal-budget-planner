using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using backend.Data;
using backend.DTOs;
using backend.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace backend.Tests;

/*
 * Test cases:
 * 1. Without a session returns 401.
 * 2. An unverified user is refused with 403.
 * 3. The preview returns the payload, its exact JSON and the report; the report shows the user's own row,
 *    the payload JSON does not contain it.
 * 4. The response is marked no-store, because the report holds the user's own data.
 * 5. Another user's rows never appear in the caller's preview.
 */
public sealed class AiPreviewEndpointTests : IClassFixture<AuthWebApplicationFactory>
{
    private const string Password = "Password1!";
    private const string Url = "/api/ai/payload-preview";

    /// <summary>The 5th of last month: always inside a complete month.</summary>
    private static readonly DateOnly LastMonth =
        new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).AddMonths(-1).AddDays(4);

    private readonly AuthWebApplicationFactory _factory;

    public AiPreviewEndpointTests(AuthWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Preview_WithoutSession_ReturnsUnauthorized()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync(Url)).StatusCode);
    }

    [Fact]
    public async Task Preview_ForUnverifiedUser_ReturnsForbidden()
    {
        var (_, email) = await SeedUserAsync(verified: false);
        var client = await SignedInAsync(email);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Url)).StatusCode);
    }

    [Fact]
    public async Task Preview_ReturnsExactPayloadJson_AndReportWithOwnRow()
    {
        var (userId, email) = await SeedUserAsync();
        var account = await AddAccountAsync(userId, "Jane's Checking");
        await AddRowAsync(userId, account, 2500m, LastMonth, "INCOME", "PAYROLL ACME CORP");
        await AddRowAsync(userId, account, -42.10m, LastMonth, "FOOD_AND_DRINK", "CORNER CAFE 5550123");
        var client = await SignedInAsync(email);

        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        var payloadJson = body["payloadJson"]!.GetValue<string>();
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(payloadJson), body["payload"]));
        Assert.DoesNotContain("CAFE", payloadJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Jane", payloadJson, StringComparison.OrdinalIgnoreCase);

        var preview = (await response.Content.ReadFromJsonAsync<AiPayloadPreviewResponse>())!;
        Assert.True(preview.Report.Passed);
        Assert.Equal(1, preview.Payload.Coverage.MonthsComplete);
        Assert.Contains(preview.Report.Examples, example =>
            example.Description == "CORNER CAFE 5550123" && example.SentAs == "FOOD_AND_DRINK");
    }

    [Fact]
    public async Task Preview_IsMarkedNoStore()
    {
        var (_, email) = await SeedUserAsync();
        var client = await SignedInAsync(email);

        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Preview_NeverIncludesAnotherUsersRows()
    {
        var (_, aliceEmail) = await SeedUserAsync();
        var (bobId, _) = await SeedUserAsync();
        var bobAccount = await AddAccountAsync(bobId, "Bob's Checking");
        await AddRowAsync(bobId, bobAccount, -900m, LastMonth, "TRAVEL", "BOB AIRLINE");
        var alice = await SignedInAsync(aliceEmail);

        var preview = (await alice.GetFromJsonAsync<AiPayloadPreviewResponse>(Url))!;

        Assert.Equal(0, preview.Payload.Coverage.MonthsComplete);
        Assert.Empty(preview.Payload.Categories);
        Assert.Empty(preview.Report.Examples);
    }

    private async Task<(Guid Id, string Email)> SeedUserAsync(bool verified = true)
    {
        var email = $"user.{Guid.NewGuid():N}@example.com";
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var user = new User { Email = email, Name = "Test User", EmailVerifiedAt = verified ? DateTime.UtcNow : null };
        user.PasswordHash = hasher.HashPassword(user, Password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (user.Id, email);
    }

    private async Task<Guid> AddAccountAsync(Guid userId, string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = new Account { UserId = userId, Name = name };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return account.Id;
    }

    private async Task AddRowAsync(Guid userId, Guid accountId, decimal amount, DateOnly date, string category, string description)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Transactions.Add(new Transaction
        {
            UserId = userId,
            AccountId = accountId,
            Source = TransactionSource.Seed,
            Description = description,
            Amount = amount,
            Date = date,
            Category = category,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Password login, plus the emailed code when the account is verified.</summary>
    private async Task<HttpClient> SignedInAsync(string email)
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        if (login.StatusCode == HttpStatusCode.Accepted)
        {
            var challenge = await login.Content.ReadFromJsonAsync<LoginChallengeResponse>();
            var code = CapturingEmailSender.CodeFrom(_factory.Emails.LastTo(email));
            login = await client.PostAsJsonAsync(
                "/api/auth/login/verify-code", new VerifyLoginCodeRequest(challenge!.ChallengeId, code));
        }

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return client;
    }
}
