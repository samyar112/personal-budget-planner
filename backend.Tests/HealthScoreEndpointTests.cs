using System.Net;
using System.Net.Http.Json;
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
 * 3. With no rows there is no score, and no indicator is scored.
 * 4. A full ledger and accounts score all eight indicators to the expected values, 71 Coping.
 * 5. Adding spending to a complete month lowers the score.
 * 6. Another user's rows and accounts never reach the caller's score.
 * 7. A late fee found by its description counts; an ATM fee does not; a card payment is not loan debt.
 */
public sealed class HealthScoreEndpointTests : IClassFixture<AuthWebApplicationFactory>
{
    private const string Password = "Password1!";
    private const string Url = "/api/transactions/health-score";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    private static readonly DateOnly MonthStart = new(Today.Year, Today.Month, 1);

    private readonly AuthWebApplicationFactory _factory;

    public HealthScoreEndpointTests(AuthWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task HealthScore_WithoutSession_ReturnsUnauthorized()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync(Url)).StatusCode);
    }

    [Fact]
    public async Task HealthScore_ForUnverifiedUser_ReturnsForbidden()
    {
        var (_, email) = await SeedUserAsync(verified: false);
        var client = await SignedInAsync(email);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Url)).StatusCode);
    }

    [Fact]
    public async Task HealthScore_WithNoRows_HasNoScore()
    {
        var (_, email) = await SeedUserAsync();
        var client = await SignedInAsync(email);

        var result = await GetAsync(client);

        Assert.Null(result.Score);
        Assert.Null(result.Tier);
        Assert.Equal(0, result.MonthsComplete);
        Assert.Equal(0, result.IndicatorsScored);
    }

    [Fact]
    public async Task HealthScore_FullLedger_ScoresEveryIndicator()
    {
        var (userId, email) = await SeedUserAsync();
        await SeedFullLedgerAsync(userId);
        var client = await SignedInAsync(email);

        var result = await GetAsync(client);

        var scores = result.Components
            .SelectMany(component => component.Indicators)
            .ToDictionary(indicator => indicator.Id, indicator => indicator.Score);
        Assert.Equal(50, scores["spend_less_than_income"]);  // keeps 10%
        Assert.Equal(67, scores["bills_on_time"]);           // one overdraft fee
        Assert.Equal(33, scores["liquid_savings"]);          // 7,200 covers 2 months of 3,600
        Assert.Equal(50, scores["long_term_savings"]);       // 24,000 is half a year of 48,000
        Assert.Equal(100, scores["manageable_debt"]);        // 600 of 4,000 is 15%
        Assert.Equal(69, scores["credit_utilisation"]);      // 300 of 1,000 is 30%
        Assert.Equal(100, scores["insurance"]);
        Assert.Equal(100, scores["plans_ahead"]);            // 3 of 3 months positive
        Assert.Equal((71, "Coping", 8, 3), (result.Score, result.Tier, result.IndicatorsScored, result.MonthsComplete));
    }

    [Fact]
    public async Task HealthScore_MoreSpending_LowersTheScore()
    {
        var (userId, email) = await SeedUserAsync();
        var checking = await SeedFullLedgerAsync(userId);
        var client = await SignedInAsync(email);
        var before = (await GetAsync(client)).Score;

        await AddRowAsync(userId, checking, -900m, MonthStart.AddMonths(-1).AddDays(10), "TRAVEL", null, "AIRLINE");
        var after = (await GetAsync(client)).Score;

        Assert.NotNull(before);
        Assert.True(after < before, $"Expected the score to fall from {before}, got {after}.");
    }

    [Fact]
    public async Task HealthScore_NeverReadsAnotherUsersData()
    {
        var (_, aliceEmail) = await SeedUserAsync();
        var (bobId, _) = await SeedUserAsync();
        await SeedFullLedgerAsync(bobId);
        var alice = await SignedInAsync(aliceEmail);

        var result = await GetAsync(alice);

        Assert.Null(result.Score);
        Assert.Equal(0, result.IndicatorsScored);
    }

    [Fact]
    public async Task HealthScore_CountsLateFeesByText_NotAtmFees_AndCardPaymentsAreNotDebt()
    {
        var (userId, email) = await SeedUserAsync();
        var checking = await AddAccountAsync(userId, "Checking", "depository", null, null);
        foreach (var month in new[] { 1, 2 })
        {
            var date = MonthStart.AddMonths(-month).AddDays(4);
            await AddRowAsync(userId, checking, 4000m, date, "INCOME", "INCOME_WAGES", "ACME PAYROLL");
            await AddRowAsync(userId, checking, -3000m, date, "RENT_AND_UTILITIES", null, "RENT");
            await AddRowAsync(userId, checking, -1500m, date, "LOAN_PAYMENTS", "LOAN_PAYMENTS_CREDIT_CARD_PAYMENT", "CARD PAYMENT");
        }
        await AddRowAsync(userId, checking, -29m, Today, "OTHER", null, "CARD LATE FEE");
        await AddRowAsync(userId, checking, -3m, Today, "BANK_FEES", "BANK_FEES_ATM_FEES", "ATM FEE");
        var client = await SignedInAsync(email);

        var result = await GetAsync(client);

        var indicators = result.Components.SelectMany(component => component.Indicators).ToList();
        Assert.Equal(67, indicators.Single(indicator => indicator.Id == "bills_on_time").Score);
        Assert.Equal(100, indicators.Single(indicator => indicator.Id == "manageable_debt").Score);
        Assert.Equal(0, indicators.Single(indicator => indicator.Id == "insurance").Score);
    }

    /// <summary>
    /// Three complete months of 4,000 in and 3,600 out (600 of it a student loan, 200 insurance), one overdraft
    /// fee this month, and checking 7,200, a brokerage 24,000 and a card 300 of 1,000. Returns the checking id.
    /// </summary>
    private async Task<Guid> SeedFullLedgerAsync(Guid userId)
    {
        var checking = await AddAccountAsync(userId, "Checking", "depository", 7200m, null);
        await AddAccountAsync(userId, "Brokerage", "investment", 24000m, null);
        await AddAccountAsync(userId, "Card", "credit", 300m, 1000m);
        foreach (var month in new[] { 1, 2, 3 })
        {
            var date = MonthStart.AddMonths(-month).AddDays(4);
            await AddRowAsync(userId, checking, 4000m, date, "INCOME", "INCOME_WAGES", "ACME PAYROLL");
            await AddRowAsync(userId, checking, -2000m, date, "RENT_AND_UTILITIES", null, "RENT");
            await AddRowAsync(userId, checking, -800m, date, "FOOD_AND_DRINK", null, "KROGER");
            await AddRowAsync(userId, checking, -600m, date, "LOAN_PAYMENTS", "LOAN_PAYMENTS_STUDENT_LOAN_PAYMENT", "STUDENT LOAN");
            await AddRowAsync(userId, checking, -200m, date, "GENERAL_SERVICES", "GENERAL_SERVICES_INSURANCE", "AUTO POLICY");
        }

        await AddRowAsync(userId, checking, -35m, Today, "BANK_FEES", "BANK_FEES_OVERDRAFT_FEES", "FEE");
        return checking;
    }

    private static async Task<HealthScoreResponse> GetAsync(HttpClient client)
    {
        var response = await client.GetAsync(Url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<HealthScoreResponse>())!;
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

    private async Task<Guid> AddAccountAsync(Guid userId, string name, string type, decimal? balance, decimal? limit)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = new Account
        {
            UserId = userId, Name = name, Type = type, CurrentBalance = balance, CreditLimit = limit,
        };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return account.Id;
    }

    private async Task AddRowAsync(
        Guid userId, Guid accountId, decimal amount, DateOnly date, string? category, string? subcategory,
        string description)
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
            Subcategory = subcategory,
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
