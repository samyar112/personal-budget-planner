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
 * 1. Every spending endpoint without a session returns 401.
 * 2. Every spending endpoint refuses an unverified user with 403.
 * 3. Categories: grouped and sorted largest first; refunds net within their category; income and
 *    internal transfers left out; cash withdrawals counted.
 * 4. Categories: the total equals the summary's expenses for the same range.
 * 5. Categories: uncategorised money out is OTHER; uncategorised money in is not spending.
 * 6. Categories: from and to bound the dates inclusively.
 * 7. accountId narrows every endpoint to that account.
 * 8. accountId belonging to another user returns 404 on every endpoint.
 * 9. Another user's rows never appear in categories, monthly or merchants.
 * 10. Monthly: one entry per calendar month in range, oldest first, empty months as zeros.
 * 11. Monthly: each month equals the summary for that month.
 * 12. Monthly: missing dates, from after to, or more than 24 months returns 400.
 * 13. Merchants: grouped by description, net of refunds, largest first, with a row count.
 * 14. Merchants: limit caps the list; payees with nothing left after refunds are left out.
 * 15. Merchants: category narrows to one category.
 */
public sealed class SpendingEndpointsTests : IClassFixture<AuthWebApplicationFactory>
{
    private const string Password = "Password1!";
    private const string Range = "from=2026-10-01&to=2026-10-31";

    private static readonly string[] Endpoints =
    [
        $"/api/transactions/spending/categories?{Range}",
        $"/api/transactions/spending/monthly?{Range}",
        $"/api/transactions/spending/merchants?{Range}",
    ];

    private readonly AuthWebApplicationFactory _factory;

    public SpendingEndpointsTests(AuthWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Spending_WithoutSession_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        foreach (var endpoint in Endpoints)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(endpoint)).StatusCode);
        }
    }

    [Fact]
    public async Task Spending_ForUnverifiedUser_ReturnsForbidden()
    {
        var (_, email) = await SeedUserAsync(verified: false);
        var client = await SignedInAsync(email);

        foreach (var endpoint in Endpoints)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(endpoint)).StatusCode);
        }
    }

    [Fact]
    public async Task Categories_GroupsNetsRefundsAndLeavesOutIncomeAndTransfers()
    {
        var (userId, email) = await SeedUserAsync();
        var account = await AddAccountAsync(userId, "Checking");
        await AddRowAsync(userId, account, 3000m, "2026-10-01", "INCOME", "INCOME_WAGES");
        await AddRowAsync(userId, account, -1200m, "2026-10-02", "RENT_AND_UTILITIES", null);
        await AddRowAsync(userId, account, -100m, "2026-10-03", "GENERAL_MERCHANDISE", null);
        await AddRowAsync(userId, account, -60m, "2026-10-04", "GENERAL_MERCHANDISE", null);
        await AddRowAsync(userId, account, 40m, "2026-10-05", "GENERAL_MERCHANDISE", null);
        await AddRowAsync(userId, account, -200m, "2026-10-06", "TRANSFER_OUT", "TRANSFER_OUT_WITHDRAWAL");
        await AddRowAsync(userId, account, -500m, "2026-10-07", "TRANSFER_OUT", "TRANSFER_OUT_ACCOUNT_TRANSFER");
        await AddRowAsync(userId, account, 250m, "2026-10-08", "TRANSFER_IN", null);
        await AddRowAsync(userId, account, -300m, "2026-10-09", "LOAN_PAYMENTS", "LOAN_PAYMENTS_CREDIT_CARD_PAYMENT");
        var client = await SignedInAsync(email);

        var result = await GetAsync<SpendingByCategoryResponse>(client, $"/api/transactions/spending/categories?{Range}");

        Assert.Equal(
            [("RENT_AND_UTILITIES", 1200m), ("TRANSFER_OUT", 200m), ("GENERAL_MERCHANDISE", 120m)],
            result.Categories.Select(category => (category.Category, category.Amount)));
        Assert.Equal(1520m, result.Total);
    }

    [Fact]
    public async Task Categories_TotalEqualsSummaryExpenses()
    {
        var (userId, email) = await SeedUserAsync();
        var checking = await AddAccountAsync(userId, "Checking");
        var card = await AddAccountAsync(userId, "Credit Card");
        await AddRowAsync(userId, checking, 4000m, "2026-10-01", "INCOME", null);
        await AddRowAsync(userId, checking, -1100m, "2026-10-02", "RENT_AND_UTILITIES", null);
        await AddRowAsync(userId, checking, -40m, "2026-10-03", "TRANSFER_OUT", "TRANSFER_OUT_WITHDRAWAL");
        await AddRowAsync(userId, checking, 25m, "2026-10-04", null, null);
        await AddRowAsync(userId, checking, -15m, "2026-10-05", null, null);
        await AddRowAsync(userId, card, -75.25m, "2026-10-06", "FOOD_AND_DRINK", null);
        await AddRowAsync(userId, card, 500m, "2026-10-07", "TRAVEL", null);
        await AddRowAsync(userId, card, -90m, "2026-10-08", "TRAVEL", null);
        var client = await SignedInAsync(email);

        var categories = await GetAsync<SpendingByCategoryResponse>(client, $"/api/transactions/spending/categories?{Range}");
        var summary = await GetAsync<TransactionSummaryResponse>(client, $"/api/transactions/summary?{Range}");

        Assert.Equal(summary.Expenses, categories.Total);
        Assert.Equal(categories.Total, categories.Categories.Sum(category => category.Amount));
    }

    [Fact]
    public async Task Categories_UncategorisedMoneyOutIsOther_MoneyInIsNotSpending()
    {
        var (userId, email) = await SeedUserAsync();
        var account = await AddAccountAsync(userId, "Checking");
        await AddRowAsync(userId, account, -30m, "2026-10-02", null, null);
        await AddRowAsync(userId, account, -20m, "2026-10-03", "OTHER", null);
        await AddRowAsync(userId, account, 500m, "2026-10-04", null, null);
        var client = await SignedInAsync(email);

        var result = await GetAsync<SpendingByCategoryResponse>(client, $"/api/transactions/spending/categories?{Range}");

        var other = Assert.Single(result.Categories);
        Assert.Equal(("OTHER", 50m), (other.Category, other.Amount));
    }

    [Fact]
    public async Task Categories_FromAndTo_BoundTheDatesInclusively()
    {
        var (userId, email) = await SeedUserAsync();
        var account = await AddAccountAsync(userId, "Checking");
        await AddRowAsync(userId, account, -1m, "2026-09-30", "FOOD_AND_DRINK", null);
        await AddRowAsync(userId, account, -2m, "2026-10-01", "FOOD_AND_DRINK", null);
        await AddRowAsync(userId, account, -4m, "2026-10-31", "FOOD_AND_DRINK", null);
        await AddRowAsync(userId, account, -8m, "2026-11-01", "FOOD_AND_DRINK", null);
        var client = await SignedInAsync(email);

        var result = await GetAsync<SpendingByCategoryResponse>(client, $"/api/transactions/spending/categories?{Range}");

        Assert.Equal(6m, result.Total);
    }

    [Fact]
    public async Task AccountId_NarrowsEveryEndpointToThatAccount()
    {
        var (userId, email) = await SeedUserAsync();
        var checking = await AddAccountAsync(userId, "Checking");
        var card = await AddAccountAsync(userId, "Credit Card");
        await AddRowAsync(userId, checking, 2000m, "2026-10-01", "INCOME", null, "ACME PAYROLL");
        await AddRowAsync(userId, checking, -800m, "2026-10-02", "RENT_AND_UTILITIES", null, "LANDLORD");
        await AddRowAsync(userId, card, -45m, "2026-10-03", "FOOD_AND_DRINK", null, "CAFE");
        var client = await SignedInAsync(email);
        var filter = $"{Range}&accountId={card}";

        var categories = await GetAsync<SpendingByCategoryResponse>(client, $"/api/transactions/spending/categories?{filter}");
        var monthly = await GetAsync<MonthlyCashFlowListResponse>(client, $"/api/transactions/spending/monthly?{filter}");
        var merchants = await GetAsync<TopMerchantsResponse>(client, $"/api/transactions/spending/merchants?{filter}");

        Assert.Equal(("FOOD_AND_DRINK", 45m), (Assert.Single(categories.Categories).Category, categories.Total));
        var october = Assert.Single(monthly.Months);
        Assert.Equal((0m, 45m, -45m), (october.Income, october.Expenses, october.NetCashFlow));
        Assert.Equal("CAFE", Assert.Single(merchants.Merchants).Merchant);
    }

    [Fact]
    public async Task AccountId_OfAnotherUser_ReturnsNotFound()
    {
        var (_, aliceEmail) = await SeedUserAsync();
        var (bobId, _) = await SeedUserAsync();
        var bobAccount = await AddAccountAsync(bobId, "Checking");
        await AddRowAsync(bobId, bobAccount, -999m, "2026-10-02", "FOOD_AND_DRINK", null);
        var alice = await SignedInAsync(aliceEmail);

        foreach (var endpoint in Endpoints)
        {
            var response = await alice.GetAsync($"{endpoint}&accountId={bobAccount}");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task Spending_NeverIncludesAnotherUsersRows()
    {
        var (aliceId, aliceEmail) = await SeedUserAsync();
        var (bobId, _) = await SeedUserAsync();
        var aliceAccount = await AddAccountAsync(aliceId, "Checking");
        var bobAccount = await AddAccountAsync(bobId, "Checking");
        await AddRowAsync(aliceId, aliceAccount, -10m, "2026-10-02", "FOOD_AND_DRINK", null, "CAFE");
        await AddRowAsync(bobId, bobAccount, -9999m, "2026-10-02", "TRAVEL", null, "AIRLINE");
        await AddRowAsync(bobId, bobAccount, 5000m, "2026-10-03", "INCOME", null, "PAYROLL");
        var alice = await SignedInAsync(aliceEmail);

        var categories = await GetAsync<SpendingByCategoryResponse>(alice, $"/api/transactions/spending/categories?{Range}");
        var monthly = await GetAsync<MonthlyCashFlowListResponse>(alice, $"/api/transactions/spending/monthly?{Range}");
        var merchants = await GetAsync<TopMerchantsResponse>(alice, $"/api/transactions/spending/merchants?{Range}");

        Assert.Equal(10m, categories.Total);
        var october = Assert.Single(monthly.Months);
        Assert.Equal((0m, 10m), (october.Income, october.Expenses));
        Assert.Equal("CAFE", Assert.Single(merchants.Merchants).Merchant);
    }

    [Fact]
    public async Task Monthly_ReturnsEveryMonthInRange_OldestFirst_EmptyMonthsAsZeros()
    {
        var (userId, email) = await SeedUserAsync();
        var account = await AddAccountAsync(userId, "Checking");
        await AddRowAsync(userId, account, 3000m, "2026-08-15", "INCOME", null);
        await AddRowAsync(userId, account, -500m, "2026-08-20", "FOOD_AND_DRINK", null);
        await AddRowAsync(userId, account, -200m, "2026-10-05", "FOOD_AND_DRINK", null);
        var client = await SignedInAsync(email);

        var result = await GetAsync<MonthlyCashFlowListResponse>(
            client, "/api/transactions/spending/monthly?from=2026-08-01&to=2026-10-31");

        Assert.Equal(
            [(2026, 8, 3000m, 500m, 2500m), (2026, 9, 0m, 0m, 0m), (2026, 10, 0m, 200m, -200m)],
            result.Months.Select(month => (month.Year, month.Month, month.Income, month.Expenses, month.NetCashFlow)));
    }

    [Fact]
    public async Task Monthly_EachMonthEqualsTheSummaryForThatMonth()
    {
        var (userId, email) = await SeedUserAsync();
        var account = await AddAccountAsync(userId, "Checking");
        await AddRowAsync(userId, account, 2500m, "2026-09-01", "INCOME", null);
        await AddRowAsync(userId, account, -40m, "2026-09-10", "TRANSFER_OUT", "TRANSFER_OUT_WITHDRAWAL");
        await AddRowAsync(userId, account, 30m, "2026-09-12", "GENERAL_MERCHANDISE", null);
        await AddRowAsync(userId, account, -600m, "2026-09-20", "TRANSFER_OUT", "TRANSFER_OUT_ACCOUNT_TRANSFER");
        await AddRowAsync(userId, account, 70m, "2026-10-02", null, null);
        await AddRowAsync(userId, account, -110m, "2026-10-03", "MEDICAL", null);
        var client = await SignedInAsync(email);

        var monthly = await GetAsync<MonthlyCashFlowListResponse>(
            client, "/api/transactions/spending/monthly?from=2026-09-01&to=2026-10-31");
        var september = await GetAsync<TransactionSummaryResponse>(client, "/api/transactions/summary?from=2026-09-01&to=2026-09-30");
        var october = await GetAsync<TransactionSummaryResponse>(client, "/api/transactions/summary?from=2026-10-01&to=2026-10-31");

        Assert.Equal(
            [(september.Income, september.Expenses, september.NetCashFlow), (october.Income, october.Expenses, october.NetCashFlow)],
            monthly.Months.Select(month => (month.Income, month.Expenses, month.NetCashFlow)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("from=2026-10-01")]
    [InlineData("to=2026-10-31")]
    [InlineData("from=2026-10-31&to=2026-10-01")]
    [InlineData("from=2024-01-01&to=2026-01-31")]
    public async Task Monthly_InvalidRange_ReturnsBadRequest(string query)
    {
        var (_, email) = await SeedUserAsync();
        var client = await SignedInAsync(email);

        var response = await client.GetAsync($"/api/transactions/spending/monthly?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Merchants_GroupedByDescription_NetOfRefunds_LargestFirst_WithCount()
    {
        var (userId, email) = await SeedUserAsync();
        var account = await AddAccountAsync(userId, "Checking");
        await AddRowAsync(userId, account, -60m, "2026-10-02", "GENERAL_MERCHANDISE", null, "AMAZON");
        await AddRowAsync(userId, account, -90m, "2026-10-03", "GENERAL_MERCHANDISE", null, "AMAZON");
        await AddRowAsync(userId, account, 30m, "2026-10-04", "GENERAL_MERCHANDISE", null, "AMAZON");
        await AddRowAsync(userId, account, -1200m, "2026-10-05", "RENT_AND_UTILITIES", null, "LANDLORD");
        await AddRowAsync(userId, account, 3000m, "2026-10-06", "INCOME", null, "ACME PAYROLL");
        await AddRowAsync(userId, account, -500m, "2026-10-07", "TRANSFER_OUT", "TRANSFER_OUT_ACCOUNT_TRANSFER", "TO SAVINGS");
        var client = await SignedInAsync(email);

        var result = await GetAsync<TopMerchantsResponse>(client, $"/api/transactions/spending/merchants?{Range}");

        Assert.Equal(
            [("LANDLORD", 1200m, 1), ("AMAZON", 120m, 3)],
            result.Merchants.Select(merchant => (merchant.Merchant, merchant.Amount, merchant.Count)));
    }

    [Fact]
    public async Task Merchants_LimitCapsTheList_AndFullyRefundedPayeesAreLeftOut()
    {
        var (userId, email) = await SeedUserAsync();
        var account = await AddAccountAsync(userId, "Checking");
        await AddRowAsync(userId, account, -50m, "2026-10-02", "GENERAL_MERCHANDISE", null, "RETURNED");
        await AddRowAsync(userId, account, 50m, "2026-10-03", "GENERAL_MERCHANDISE", null, "RETURNED");
        await AddRowAsync(userId, account, -30m, "2026-10-04", "FOOD_AND_DRINK", null, "A");
        await AddRowAsync(userId, account, -20m, "2026-10-05", "FOOD_AND_DRINK", null, "B");
        await AddRowAsync(userId, account, -10m, "2026-10-06", "FOOD_AND_DRINK", null, "C");
        var client = await SignedInAsync(email);

        var result = await GetAsync<TopMerchantsResponse>(client, $"/api/transactions/spending/merchants?{Range}&limit=2");

        Assert.Equal(["A", "B"], result.Merchants.Select(merchant => merchant.Merchant));
    }

    [Fact]
    public async Task Merchants_Category_NarrowsToOneCategory()
    {
        var (userId, email) = await SeedUserAsync();
        var account = await AddAccountAsync(userId, "Checking");
        await AddRowAsync(userId, account, -1200m, "2026-10-02", "RENT_AND_UTILITIES", null, "LANDLORD");
        await AddRowAsync(userId, account, -12m, "2026-10-03", "FOOD_AND_DRINK", null, "CAFE");
        var client = await SignedInAsync(email);

        var result = await GetAsync<TopMerchantsResponse>(
            client, $"/api/transactions/spending/merchants?{Range}&category=FOOD_AND_DRINK");

        Assert.Equal("CAFE", Assert.Single(result.Merchants).Merchant);
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>())!;
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
        Guid userId, Guid accountId, decimal amount, string date, string? category, string? subcategory,
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
            Date = DateOnly.Parse(date),
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
