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
 * 1. A summary without a session returns 401.
 * 2. An unverified user is refused with 403.
 * 3. Income is all money in, expenses is all money out, and savings is the difference.
 * 4. Transfers (in and out) and credit card payments are left out of every figure.
 * 5. Other loan payments still count as expenses.
 * 6. A credit that is not categorised INCOME, such as a refund or deposit, counts as income; expenses never go negative.
 * 7. A row with no category counts by sign.
 * 8. Each account gets its own figures, and the totals equal the sum of the accounts.
 * 9. An account with no rows in range still appears, with zeros.
 * 10. from and to bound the dates inclusively.
 * 11. Another user's rows and accounts never appear or affect the totals.
 * 12. An empty ledger returns zeros and no accounts.
 * 13. For the same range, the summary equals the list rows minus the excluded rows.
 */
public sealed class TransactionSummaryEndpointTests : IClassFixture<AuthWebApplicationFactory>
{
    private const string Password = "Password1!";
    private const string Range = "from=2026-10-01&to=2026-10-31";

    private readonly AuthWebApplicationFactory _factory;

    public TransactionSummaryEndpointTests(AuthWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Summary_WithoutSession_ReturnsUnauthorized()
    {
        var response = await _factory.CreateClient().GetAsync($"/api/transactions/summary?{Range}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Summary_ForUnverifiedUser_ReturnsForbidden()
    {
        var (_, email) = await SeedUserAsync(verified: false);
        var client = await SignedInAsync(email);

        var response = await client.GetAsync($"/api/transactions/summary?{Range}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Summary_IncomeIsMoneyIn_ExpensesIsMoneyOut()
    {
        var (userId, email) = await SeedUserAsync();
        var account = await AddAccountAsync(userId, "Checking");
        await AddRowAsync(userId, account, 3000m, "2026-10-01", "INCOME", "INCOME_WAGES");
        await AddRowAsync(userId, account, -1200m, "2026-10-02", "RENT_AND_UTILITIES", null);
        await AddRowAsync(userId, account, -300m, "2026-10-03", "FOOD_AND_DRINK", "FOOD_AND_DRINK_GROCERIES");
        var client = await SignedInAsync(email);

        var summary = await GetSummaryAsync(client);

        Assert.Equal(3000m, summary.Income);
        Assert.Equal(1500m, summary.Expenses);
        Assert.Equal(1500m, summary.Savings);
    }

    [Fact]
    public async Task Summary_LeavesOutTransfersAndCreditCardPayments()
    {
        var (userId, email) = await SeedUserAsync();
        var checking = await AddAccountAsync(userId, "Checking");
        var card = await AddAccountAsync(userId, "Credit Card");
        await AddRowAsync(userId, checking, 2000m, "2026-10-01", "INCOME", null);
        await AddRowAsync(userId, checking, -500m, "2026-10-02", "TRANSFER_OUT", "TRANSFER_OUT_ACCOUNT_TRANSFER");
        await AddRowAsync(userId, checking, 250m, "2026-10-03", "TRANSFER_IN", "TRANSFER_IN_ACCOUNT_TRANSFER");
        await AddRowAsync(userId, checking, -400m, "2026-10-04", "LOAN_PAYMENTS", "LOAN_PAYMENTS_CREDIT_CARD_PAYMENT");
        await AddRowAsync(userId, card, 400m, "2026-10-04", "LOAN_PAYMENTS", "LOAN_PAYMENTS_CREDIT_CARD_PAYMENT");
        await AddRowAsync(userId, card, -60m, "2026-10-05", "FOOD_AND_DRINK", null);
        var client = await SignedInAsync(email);

        var summary = await GetSummaryAsync(client);

        Assert.Equal(2000m, summary.Income);
        Assert.Equal(60m, summary.Expenses);
        Assert.Equal(1940m, summary.Savings);
    }

    [Fact]
    public async Task Summary_OtherLoanPayments_StillCountAsExpenses()
    {
        var (userId, email) = await SeedUserAsync();
        var account = await AddAccountAsync(userId, "Checking");
        await AddRowAsync(userId, account, -700m, "2026-10-02", "LOAN_PAYMENTS", "LOAN_PAYMENTS_MORTGAGE_PAYMENT");
        var client = await SignedInAsync(email);

        var summary = await GetSummaryAsync(client);

        Assert.Equal(700m, summary.Expenses);
    }

    [Fact]
    public async Task Summary_Refund_CountsAsIncome_AndExpensesStayPositive()
    {
        var (userId, email) = await SeedUserAsync();
        var account = await AddAccountAsync(userId, "Credit Card");
        await AddRowAsync(userId, account, -100m, "2026-10-02", "GENERAL_MERCHANDISE", null);
        await AddRowAsync(userId, account, 30m, "2026-10-04", "GENERAL_MERCHANDISE", null);
        var client = await SignedInAsync(email);

        var summary = await GetSummaryAsync(client);

        Assert.Equal(30m, summary.Income);
        Assert.Equal(100m, summary.Expenses);
        Assert.Equal(-70m, summary.Savings);
    }

    [Fact]
    public async Task Summary_DepositThatIsNotCategorisedIncome_CountsAsIncome_NeverNegativeExpenses()
    {
        var (userId, email) = await SeedUserAsync();
        var account = await AddAccountAsync(userId, "Checking");
        await AddRowAsync(userId, account, 500m, "2026-10-02", "TRAVEL", "TRAVEL_FLIGHTS");
        await AddRowAsync(userId, account, -50m, "2026-10-03", "FOOD_AND_DRINK", null);
        var client = await SignedInAsync(email);

        var summary = await GetSummaryAsync(client);

        Assert.Equal(500m, summary.Income);
        Assert.Equal(50m, summary.Expenses);
        Assert.Equal(450m, summary.Savings);
        var figures = Assert.Single(summary.Accounts);
        Assert.Equal((500m, 50m, 450m), (figures.Income, figures.Expenses, figures.Net));
    }

    [Fact]
    public async Task Summary_UncategorisedRows_CountBySign()
    {
        var (userId, email) = await SeedUserAsync();
        var account = await AddAccountAsync(userId, "Checking");
        await AddRowAsync(userId, account, 500m, "2026-10-02", null, null);
        await AddRowAsync(userId, account, -120m, "2026-10-03", null, null);
        var client = await SignedInAsync(email);

        var summary = await GetSummaryAsync(client);

        Assert.Equal(500m, summary.Income);
        Assert.Equal(120m, summary.Expenses);
    }

    [Fact]
    public async Task Summary_GivesEachAccountItsFigures_AndTotalsAreTheirSum()
    {
        var (userId, email) = await SeedUserAsync();
        var checking = await AddAccountAsync(userId, "Checking");
        var card = await AddAccountAsync(userId, "Credit Card");
        await AddRowAsync(userId, checking, 2500m, "2026-10-01", "INCOME", null);
        await AddRowAsync(userId, checking, -800m, "2026-10-05", "RENT_AND_UTILITIES", null);
        await AddRowAsync(userId, card, -45m, "2026-10-06", "FOOD_AND_DRINK", null);
        await AddRowAsync(userId, card, -55m, "2026-10-07", "ENTERTAINMENT", null);
        var client = await SignedInAsync(email);

        var summary = await GetSummaryAsync(client);

        var checkingFigures = summary.Accounts.Single(account => account.AccountId == checking);
        Assert.Equal((2500m, 800m, 1700m), (checkingFigures.Income, checkingFigures.Expenses, checkingFigures.Net));
        var cardFigures = summary.Accounts.Single(account => account.AccountId == card);
        Assert.Equal((0m, 100m, -100m), (cardFigures.Income, cardFigures.Expenses, cardFigures.Net));
        Assert.Equal(summary.Accounts.Sum(account => account.Income), summary.Income);
        Assert.Equal(summary.Accounts.Sum(account => account.Expenses), summary.Expenses);
        Assert.Equal(summary.Accounts.Sum(account => account.Net), summary.Savings);
    }

    [Fact]
    public async Task Summary_AccountWithNoRowsInRange_StillAppearsWithZeros()
    {
        var (userId, email) = await SeedUserAsync();
        var busy = await AddAccountAsync(userId, "Checking");
        var quiet = await AddAccountAsync(userId, "Saving");
        await AddRowAsync(userId, busy, -10m, "2026-10-02", "FOOD_AND_DRINK", null);
        await AddRowAsync(userId, quiet, 99m, "2026-09-15", "INCOME", null);
        var client = await SignedInAsync(email);

        var summary = await GetSummaryAsync(client);

        var figures = summary.Accounts.Single(account => account.AccountId == quiet);
        Assert.Equal((0m, 0m, 0m), (figures.Income, figures.Expenses, figures.Net));
    }

    [Fact]
    public async Task Summary_FromAndTo_BoundTheDatesInclusively()
    {
        var (userId, email) = await SeedUserAsync();
        var account = await AddAccountAsync(userId, "Checking");
        await AddRowAsync(userId, account, -1m, "2026-09-30", "FOOD_AND_DRINK", null);
        await AddRowAsync(userId, account, -2m, "2026-10-01", "FOOD_AND_DRINK", null);
        await AddRowAsync(userId, account, -4m, "2026-10-31", "FOOD_AND_DRINK", null);
        await AddRowAsync(userId, account, -8m, "2026-11-01", "FOOD_AND_DRINK", null);
        var client = await SignedInAsync(email);

        var summary = await GetSummaryAsync(client);

        Assert.Equal(6m, summary.Expenses);
    }

    [Fact]
    public async Task Summary_NeverIncludesAnotherUsersRowsOrAccounts()
    {
        var (aliceId, aliceEmail) = await SeedUserAsync();
        var (bobId, _) = await SeedUserAsync();
        var aliceAccount = await AddAccountAsync(aliceId, "Checking");
        var bobAccount = await AddAccountAsync(bobId, "Checking");
        await AddRowAsync(aliceId, aliceAccount, -10m, "2026-10-02", "FOOD_AND_DRINK", null);
        await AddRowAsync(bobId, bobAccount, -9999m, "2026-10-02", "FOOD_AND_DRINK", null);
        await AddRowAsync(bobId, bobAccount, 5000m, "2026-10-03", "INCOME", null);
        var alice = await SignedInAsync(aliceEmail);

        var summary = await GetSummaryAsync(alice);

        Assert.Equal(10m, summary.Expenses);
        Assert.Equal(0m, summary.Income);
        Assert.Equal(aliceAccount, Assert.Single(summary.Accounts).AccountId);
    }

    [Fact]
    public async Task Summary_EmptyLedger_ReturnsZerosAndNoAccounts()
    {
        var (_, email) = await SeedUserAsync();
        var client = await SignedInAsync(email);

        var summary = await GetSummaryAsync(client);

        Assert.Equal((0m, 0m, 0m), (summary.Income, summary.Expenses, summary.Savings));
        Assert.Empty(summary.Accounts);
    }

    [Fact]
    public async Task Summary_EqualsTheListMinusTheExcludedRows()
    {
        var (userId, email) = await SeedUserAsync();
        var account = await AddAccountAsync(userId, "Checking");
        await AddRowAsync(userId, account, 4000m, "2026-10-01", "INCOME", null);
        await AddRowAsync(userId, account, -1100m, "2026-10-02", "RENT_AND_UTILITIES", null);
        await AddRowAsync(userId, account, -75.25m, "2026-10-03", "FOOD_AND_DRINK", null);
        await AddRowAsync(userId, account, -600m, "2026-10-04", "TRANSFER_OUT", null);
        await AddRowAsync(userId, account, -250m, "2026-10-05", "LOAN_PAYMENTS", "LOAN_PAYMENTS_CREDIT_CARD_PAYMENT");
        var client = await SignedInAsync(email);

        var summary = await GetSummaryAsync(client);
        var list = (await client.GetFromJsonAsync<TransactionListResponse>($"/api/transactions?{Range}&pageSize=100"))!;

        // The list is the source of truth: drop what summaries exclude and the rest must add up to Savings.
        var counted = list.Items
            .Where(row => row.Category is not ("TRANSFER_IN" or "TRANSFER_OUT")
                && row.Subcategory != "LOAN_PAYMENTS_CREDIT_CARD_PAYMENT")
            .ToList();
        Assert.Equal(5, list.Items.Count);
        Assert.Equal(3, counted.Count);
        Assert.Equal(counted.Sum(row => row.Amount), summary.Savings);
        Assert.Equal(counted.Where(row => row.Amount > 0).Sum(row => row.Amount), summary.Income);
        Assert.Equal(-counted.Where(row => row.Amount < 0).Sum(row => row.Amount), summary.Expenses);
    }

    private async Task<TransactionSummaryResponse> GetSummaryAsync(HttpClient client)
    {
        var response = await client.GetAsync($"/api/transactions/summary?{Range}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TransactionSummaryResponse>())!;
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
        Guid userId, Guid accountId, decimal amount, string date, string? category, string? subcategory)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Transactions.Add(new Transaction
        {
            UserId = userId,
            AccountId = accountId,
            Source = TransactionSource.Seed,
            Description = "row",
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
