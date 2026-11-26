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
 * 3. A monthly charge seen three times comes back as a stream, and the monthly total sums the streams.
 * 4. Income, own-account transfers, credit card payments and refunds never become bills.
 * 5. Another user's charges never appear.
 * 6. Charges older than the 13-month window are not read.
 */
public sealed class RecurringEndpointTests : IClassFixture<AuthWebApplicationFactory>
{
    private const string Password = "Password1!";
    private const string Url = "/api/transactions/recurring";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly AuthWebApplicationFactory _factory;

    public RecurringEndpointTests(AuthWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Recurring_WithoutSession_ReturnsUnauthorized()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync(Url)).StatusCode);
    }

    [Fact]
    public async Task Recurring_ForUnverifiedUser_ReturnsForbidden()
    {
        var (_, email) = await SeedUserAsync(verified: false);
        var client = await SignedInAsync(email);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Url)).StatusCode);
    }

    [Fact]
    public async Task Recurring_MonthlyCharge_IsAStream_AndTotalSumsMonthlyCosts()
    {
        var (userId, email) = await SeedUserAsync();
        var account = await AddAccountAsync(userId, "Card");
        foreach (var days in new[] { 64, 34, 4 })
        {
            await AddRowAsync(userId, account, -15.49m, Today.AddDays(-days), "ENTERTAINMENT", null, "NETFLIX.COM");
            await AddRowAsync(userId, account, -60m, Today.AddDays(-days + 1), "RENT_AND_UTILITIES", null, "INTERNET CO");
        }
        await AddRowAsync(userId, account, -82m, Today.AddDays(-10), "FOOD_AND_DRINK", null, "KROGER");
        var client = await SignedInAsync(email);

        var result = await GetAsync(client);

        Assert.Equal(["NETFLIX.COM", "INTERNET CO"], result.Streams.Select(stream => stream.Merchant));
        var netflix = result.Streams[0];
        Assert.Equal(("monthly", 15.49m, 3, true), (netflix.Cadence, netflix.TypicalAmount, netflix.Occurrences, netflix.IsSubscription));
        Assert.Equal(Today.AddDays(-4).AddMonths(1), netflix.NextDate);
        Assert.Equal(75.49m, result.MonthlyTotal);
    }

    [Fact]
    public async Task Recurring_IgnoresIncomeTransfersCardPaymentsAndRefunds()
    {
        var (userId, email) = await SeedUserAsync();
        var account = await AddAccountAsync(userId, "Checking");
        foreach (var days in new[] { 64, 34, 4 })
        {
            var date = Today.AddDays(-days);
            await AddRowAsync(userId, account, 3000m, date, "INCOME", "INCOME_WAGES", "ACME PAYROLL");
            await AddRowAsync(userId, account, -500m, date, "TRANSFER_OUT", "TRANSFER_OUT_ACCOUNT_TRANSFER", "TO SAVINGS");
            await AddRowAsync(userId, account, -400m, date, "LOAN_PAYMENTS", "LOAN_PAYMENTS_CREDIT_CARD_PAYMENT", "CARD PAYMENT");
            await AddRowAsync(userId, account, 20m, date, "GENERAL_MERCHANDISE", null, "STORE REFUND");
        }
        var client = await SignedInAsync(email);

        Assert.Empty((await GetAsync(client)).Streams);
    }

    [Fact]
    public async Task Recurring_NeverIncludesAnotherUsersCharges()
    {
        var (_, aliceEmail) = await SeedUserAsync();
        var (bobId, _) = await SeedUserAsync();
        var bobAccount = await AddAccountAsync(bobId, "Card");
        foreach (var days in new[] { 64, 34, 4 })
        {
            await AddRowAsync(bobId, bobAccount, -15.49m, Today.AddDays(-days), "ENTERTAINMENT", null, "NETFLIX.COM");
        }
        var alice = await SignedInAsync(aliceEmail);

        var result = await GetAsync(alice);

        Assert.Empty(result.Streams);
        Assert.Equal(0m, result.MonthlyTotal);
    }

    [Fact]
    public async Task Recurring_DoesNotReadChargesOutsideTheWindow()
    {
        var (userId, email) = await SeedUserAsync();
        var account = await AddAccountAsync(userId, "Card");
        // Yearly, but the first charge is 2 years back: only two are in the window, so no stream.
        foreach (var years in new[] { 2, 1 })
        {
            await AddRowAsync(userId, account, -120m, Today.AddDays(-3).AddYears(-years), "GENERAL_SERVICES", null, "DOMAIN HOST");
        }
        await AddRowAsync(userId, account, -120m, Today.AddDays(-3), "GENERAL_SERVICES", null, "DOMAIN HOST");
        var client = await SignedInAsync(email);

        Assert.Empty((await GetAsync(client)).Streams);
    }

    private static async Task<RecurringListResponse> GetAsync(HttpClient client)
    {
        var response = await client.GetAsync(Url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RecurringListResponse>())!;
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

    private async Task AddRowAsync(
        Guid userId, Guid accountId, decimal amount, DateOnly date, string? category, string? subcategory,
        string description = "row")
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
