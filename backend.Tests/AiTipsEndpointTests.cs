using System.Net;
using System.Net.Http.Json;
using backend.Data;
using backend.DTOs;
using backend.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace backend.Tests;

/*
 * Test cases:
 * 1. Without a session returns 401.
 * 2. An unverified user is refused with 403.
 * 3. Without consent returns 400; the model is not called and nothing is recorded.
 * 4. When the privacy check fails returns 422; the model is not called and nothing is recorded.
 * 5. A valid model reply returns 3-5 AI tips and records the model, prompt version and payload hash.
 * 6. A reply with a number the payload does not hold falls back to template tips.
 * 7. A provider failure falls back to template tips with 200.
 * 8. What is sent to the model holds no description or account name.
 * 9. Past the daily limit returns 429 and the model is not called.
 * 10. Another user's rows never reach the caller's request to the model.
 * 11. One invalid tip in the reply falls back to its own template; the others keep the AI wording ("mixed").
 * 12. The buffer tip carries its roadmap steps, and the steps are never sent to the model.
 */
public sealed class AiTipsEndpointTests : IClassFixture<AuthWebApplicationFactory>
{
    private const string Password = "Password1!";
    private const string Url = "/api/ai/tips";

    /// <summary>The 5th of last month: always inside a complete month.</summary>
    private static readonly DateOnly LastMonth =
        new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).AddMonths(-1).AddDays(4);

    private static readonly GenerateTipsRequest Consented = new(true);

    private readonly AuthWebApplicationFactory _factory;

    public AiTipsEndpointTests(AuthWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.Advisor.Reset();
    }

    [Fact]
    public async Task Tips_WithoutSession_ReturnsUnauthorized()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(Url, Consented);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Tips_ForUnverifiedUser_ReturnsForbidden()
    {
        var (_, email) = await SeedUserAsync(verified: false);
        var client = await SignedInAsync(email);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(Url, Consented)).StatusCode);
    }

    [Fact]
    public async Task Tips_WithoutConsent_ReturnsBadRequest_AndSendsNothing()
    {
        var (userId, email) = await SeedUserWithLedgerAsync();
        var client = await SignedInAsync(email);
        var before = _factory.Advisor.Requests.Count;

        var response = await client.PostAsJsonAsync(Url, new GenerateTipsRequest(false));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, _factory.Advisor.Requests.Count);
        Assert.Equal(0, await CountRowsAsync(userId));
    }

    [Fact]
    public async Task Tips_WhenPrivacyCheckFails_ReturnsUnprocessable_AndSendsNothing()
    {
        var (userId, email) = await SeedUserWithLedgerAsync();
        var account = await AddAccountAsync(userId, "Checking");
        // A category outside our own codes reaches the payload as unknown text, which the check blocks.
        await AddRowAsync(userId, account, -50m, LastMonth, "JANE_DOE_CUSTOM", "GIFT");
        var client = await SignedInAsync(email);
        var before = _factory.Advisor.Requests.Count;

        var response = await client.PostAsJsonAsync(Url, Consented);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(before, _factory.Advisor.Requests.Count);
        Assert.Equal(0, await CountRowsAsync(userId));
    }

    [Fact]
    public async Task Tips_WithValidReply_ReturnsAiTips_AndRecordsTheGeneration()
    {
        var (userId, email) = await SeedUserWithLedgerAsync();
        var client = await SignedInAsync(email);

        var response = await client.PostAsJsonAsync(Url, Consented);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = (await response.Content.ReadFromJsonAsync<AiTipsResponse>())!;
        Assert.Equal("ai", body.Source);
        Assert.Equal(FakeAdvisorModel.ModelName, body.Model);
        Assert.InRange(body.Tips.Count, 3, 5);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.AiRecommendations.SingleAsync(recommendation => recommendation.UserId == userId);
        Assert.Equal(TipSource.Ai, row.Source);
        Assert.Equal(FakeAdvisorModel.ModelName, row.Model);
        Assert.Equal("tips.v1", row.PromptVersion);
        Assert.Matches("^[0-9a-f]{64}$", row.PayloadSha256);
    }

    [Fact]
    public async Task Tips_WithInventedNumber_FallsBackToTemplates()
    {
        var (userId, email) = await SeedUserWithLedgerAsync();
        var client = await SignedInAsync(email);
        // Every digit becomes 9, so every tip holds a number the payload does not.
        _factory.Advisor.Reply = content => System.Text.RegularExpressions.Regex.Replace(FakeAdvisorModel.Echo(content), @"\d", "9");

        var response = await client.PostAsJsonAsync(Url, Consented);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<AiTipsResponse>())!;
        Assert.Equal("template", body.Source);
        Assert.Null(body.Model);
        Assert.InRange(body.Tips.Count, 3, 5);
        Assert.All(body.Tips, tip => Assert.Equal("template", tip.Source));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.AiRecommendations.SingleAsync(recommendation => recommendation.UserId == userId);
        Assert.Equal(TipSource.Template, row.Source);
        Assert.Null(row.Model);
    }

    [Fact]
    public async Task Tips_WithOneInvalidTip_KeepsTheRestAsAi()
    {
        var (_, email) = await SeedUserWithLedgerAsync();
        var client = await SignedInAsync(email);
        // Invent a number in the first tip only.
        _factory.Advisor.Reply = content =>
        {
            var echo = FakeAdvisorModel.Echo(content);
            var first = echo.IndexOf('$');
            return echo[..first] + "$9" + echo[(first + 1)..];
        };

        var response = await client.PostAsJsonAsync(Url, Consented);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<AiTipsResponse>())!;
        Assert.Equal("mixed", body.Source);
        Assert.Equal(FakeAdvisorModel.ModelName, body.Model);
        Assert.Equal(3, body.Tips.Count);
        Assert.Equal(2, body.Tips.Count(tip => tip.Source == "ai"));
        Assert.DoesNotContain(body.Tips, tip => tip.Text.Contains("$9", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Tips_BufferRoadmap_IsReturned_ButNeverSentToTheModel()
    {
        var (_, email) = await SeedUserWithLedgerAsync();
        var client = await SignedInAsync(email);
        var before = _factory.Advisor.Requests.Count;

        var body = (await (await client.PostAsJsonAsync(Url, Consented)).Content.ReadFromJsonAsync<AiTipsResponse>())!;

        var buffer = body.Tips.Single(tip => tip.Id == "EMERGENCY_BUFFER");
        Assert.Equal(4, buffer.Steps.Count);
        var sent = _factory.Advisor.Requests.Skip(before).Single();
        Assert.DoesNotContain("steps", sent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("of spending\"", sent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tips_WhenProviderFails_FallsBackToTemplates()
    {
        var (_, email) = await SeedUserWithLedgerAsync();
        var client = await SignedInAsync(email);
        _factory.Advisor.Fail = true;

        var response = await client.PostAsJsonAsync(Url, Consented);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<AiTipsResponse>())!;
        Assert.Equal("template", body.Source);
        Assert.InRange(body.Tips.Count, 3, 5);
    }

    [Fact]
    public async Task Tips_RequestToModel_HoldsNoIdentifyingText()
    {
        var (_, email) = await SeedUserWithLedgerAsync();
        var client = await SignedInAsync(email);
        var before = _factory.Advisor.Requests.Count;

        await client.PostAsJsonAsync(Url, Consented);

        var sent = _factory.Advisor.Requests.Skip(before).Single();
        Assert.DoesNotContain("CORNER CAFE", sent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Jane", sent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(email, sent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Tips_PastDailyLimit_ReturnsTooManyRequests()
    {
        var (_, email) = await SeedUserWithLedgerAsync();
        var client = await SignedInAsync(email);
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(Url, Consented)).StatusCode);
        }

        var before = _factory.Advisor.Requests.Count;
        var response = await client.PostAsJsonAsync(Url, Consented);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(before, _factory.Advisor.Requests.Count);
    }

    [Fact]
    public async Task Tips_NeverUseAnotherUsersRows()
    {
        var (_, aliceEmail) = await SeedUserAsync();
        var (bobId, _) = await SeedUserWithLedgerAsync();
        var bobAccount = await AddAccountAsync(bobId, "Bob's Checking");
        await AddRowAsync(bobId, bobAccount, -900m, LastMonth, "TRAVEL", "BOB AIRLINE");
        var alice = await SignedInAsync(aliceEmail);
        var before = _factory.Advisor.Requests.Count;

        var response = await alice.PostAsJsonAsync(Url, Consented);

        // Alice has no rows, so there are no candidates and the model is never asked.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<AiTipsResponse>())!;
        Assert.Empty(body.Tips);
        Assert.Equal(before, _factory.Advisor.Requests.Count);
    }

    /// <summary>Income, rent and food last month: enough for three or more rule candidates.</summary>
    private async Task<(Guid Id, string Email)> SeedUserWithLedgerAsync()
    {
        var (userId, email) = await SeedUserAsync();
        var account = await AddAccountAsync(userId, "Jane's Checking");
        await AddRowAsync(userId, account, 3000m, LastMonth, "INCOME", "PAYROLL ACME CORP");
        await AddRowAsync(userId, account, -1200m, LastMonth, "RENT_AND_UTILITIES", "LANDLORD LLC");
        await AddRowAsync(userId, account, -412.35m, LastMonth, "FOOD_AND_DRINK", "CORNER CAFE 5550123");
        return (userId, email);
    }

    private async Task<int> CountRowsAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AiRecommendations.CountAsync(row => row.UserId == userId);
    }

    private async Task<(Guid Id, string Email)> SeedUserAsync(bool verified = true)
    {
        var email = $"user.{Guid.NewGuid():N}@example.com";
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var user = new User { Email = email, Name = "Jane Tester", EmailVerifiedAt = verified ? DateTime.UtcNow : null };
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

