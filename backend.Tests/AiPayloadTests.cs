using backend.Data;
using backend.DTOs;
using backend.Models;
using backend.Services;
using Microsoft.Extensions.DependencyInjection;

namespace backend.Tests;

/*
 * Test cases:
 * 1. No description, merchant, account name, bank, mask, balance, name or email reaches the payload,
 *    even when descriptions hold an email, a card number, a phone number and a person's name.
 * 2. Metrics average the complete months that have data, follow the summary rules, and skip the current month.
 * 3. Recurring streams arrive as category, cadence and cost, with no merchant.
 * 4. The report's withheld counts, examples and checks match the seeded ledger.
 * 5. Another user's rows never reach the payload.
 * 6. A user with no rows gets null metrics and a passing report.
 * 7. The validator rejects each kind of identifying string, one check per kind.
 * 8. The validator passes a clean payload with dates and six-figure amounts.
 */
public sealed class AiPayloadTests : IClassFixture<AuthWebApplicationFactory>
{
    private static readonly DateOnly Today = new(2026, 11, 3);

    /// <summary>Text from the seed that must never appear in the payload, checked case-insensitively.</summary>
    private static readonly string[] Secrets =
    [
        "jane", "doe", "acme", "netflix", "866", "zelle", "john", "smith", "4111", "kroger", "whole foods",
        "everyday", "platypus", "platinum", "6789", "4242", "1234.56",
    ];

    private readonly AuthWebApplicationFactory _factory;

    public AiPayloadTests(AuthWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Payload_ContainsNoIdentifyingText()
    {
        var userId = await SeedJaneAsync();

        var result = await BuildAsync(userId);
        var json = PayloadPrivacyValidator.Serialize(result.Payload);

        foreach (var secret in Secrets)
        {
            Assert.DoesNotContain(secret, json, StringComparison.OrdinalIgnoreCase);
        }
        Assert.True(result.Report.Passed);
    }

    [Fact]
    public async Task Payload_MetricsAverageCompleteMonths_ByTheSummaryRules()
    {
        var userId = await SeedJaneAsync();

        var payload = (await BuildAsync(userId)).Payload;

        // Aug to Oct: three complete months. Nov is partial and April is outside the six-month window.
        Assert.Equal(3, payload.Coverage.MonthsComplete);
        Assert.Equal(["checking", "credit card"], payload.Coverage.AccountTypes);
        Assert.Equal(3000m, Metric(payload, AiPayloadBuilder.IncomeMonthlyMean));
        Assert.Equal(300m, Metric(payload, AiPayloadBuilder.SpendingMonthlyMean));
        Assert.Equal(2700m, Metric(payload, AiPayloadBuilder.NetCashFlowMean));
        Assert.Equal(0.9m, Metric(payload, AiPayloadBuilder.SavingsRate));
        // The $500 move to savings is an own-account transfer, so it is not spending. Zelle to a person is.
        Assert.Equal(
            [("TRANSFER_OUT", 200m), ("FOOD_AND_DRINK", 84.51m), ("ENTERTAINMENT", 15.49m)],
            payload.Categories.Select(category => (category.Code, category.MonthlyMean)));
    }

    [Fact]
    public async Task Payload_RecurringStreams_CarryNoMerchant()
    {
        var userId = await SeedJaneAsync();

        var recurring = (await BuildAsync(userId)).Payload.Recurring;

        Assert.Contains(new PayloadRecurring("ENTERTAINMENT", "monthly", 15.49m, true), recurring);
        Assert.Equal(recurring.OrderByDescending(stream => stream.MonthlyCost), recurring);
    }

    [Fact]
    public async Task Report_CountsWithheldFields_AndShowsRealExamples()
    {
        var userId = await SeedJaneAsync();

        var report = (await BuildAsync(userId)).Report;

        Assert.Equal(
            [
                ("Transaction descriptions", 14),
                ("Payees on person-to-person transfers", 3),
                ("Account names", 2),
                ("Bank names", 1),
                ("Account number endings", 2),
                ("Account balances", 1),
            ],
            report.Withheld.Select(field => (field.Field, field.Count)));

        Assert.Equal(3, report.Examples.Count);
        var newest = report.Examples[0];
        Assert.Equal(
            (new DateOnly(2026, 11, 1), "WHOLE FOODS MKT 10234", "Platinum Rewards Card", "4242", "FOOD_AND_DRINK"),
            (newest.Date, newest.Description, newest.AccountName, newest.Mask, newest.SentAs));
        Assert.Equal(3, report.Examples.Select(example => example.SentAs).Distinct().Count());

        Assert.Equal(6, report.Checks.Count);
        Assert.All(report.Checks, check => Assert.Equal((0, true), (check.Hits, check.Passed)));
    }

    [Fact]
    public async Task Payload_NeverIncludesAnotherUsersRows()
    {
        var janeId = await SeedJaneAsync();
        var bobId = await AddUserAsync("Bob");
        var bobAccount = await AddAccountAsync(bobId, "Bob Secret Checking", null, null, "depository", "savings", null);
        await AddRowAsync(bobId, bobAccount, 50000m, new DateOnly(2026, 9, 1), "INCOME", null, "BOB SECRET PAYROLL");
        await AddRowAsync(bobId, bobAccount, -900m, new DateOnly(2026, 9, 2), "TRAVEL", null, "BOB SECRET AIRLINE");

        var result = await BuildAsync(janeId);

        Assert.Equal(3000m, Metric(result.Payload, AiPayloadBuilder.IncomeMonthlyMean));
        Assert.DoesNotContain(result.Payload.Categories, category => category.Code == "TRAVEL");
        Assert.DoesNotContain("savings", result.Payload.Coverage.AccountTypes);
        Assert.DoesNotContain(result.Report.Examples, example => example.Description.Contains("BOB"));
    }

    [Fact]
    public async Task Payload_ForEmptyLedger_HasNullMetrics_AndPasses()
    {
        var userId = await AddUserAsync("Empty");

        var result = await BuildAsync(userId);

        Assert.Equal(0, result.Payload.Coverage.MonthsComplete);
        Assert.All(result.Payload.Metrics, metric => Assert.Null(metric.Value));
        Assert.Empty(result.Payload.Categories);
        Assert.Empty(result.Payload.Recurring);
        Assert.Empty(result.Report.Examples);
        Assert.True(result.Report.Passed);
    }

    [Theory]
    [InlineData("jane.doe@gmail.com", "email")]
    [InlineData("4111111111111111", "digit_run")]
    [InlineData("https://tracker.example", "url")]
    [InlineData("netflix.com", "url")]
    [InlineData("+1 (316) 555-0100", "phone")]
    [InlineData("Jane's Everyday Checking", "user_identifier")]
    [InlineData("WALMART", "unknown_text")]
    public void Validator_RejectsIdentifyingText(string leaked, string expectedCheck)
    {
        var payload = CleanPayload() with { Categories = [new PayloadCategory(leaked, 10m)] };

        var checks = PayloadPrivacyValidator.Validate(payload, ["Jane's Everyday Checking"]);

        var failed = Assert.Single(checks, check => check.Id == expectedCheck);
        Assert.Equal((1, false), (failed.Hits, failed.Passed));
    }

    [Fact]
    public void Validator_PassesCleanPayload()
    {
        // A description equal to one of our codes is not a leak: the code is ours either way.
        var checks = PayloadPrivacyValidator.Validate(CleanPayload(), ["TRAVEL", "Jane's Everyday Checking", "6789"]);

        Assert.All(checks, check => Assert.True(check.Passed, check.Id));
    }

    private static AiPayload CleanPayload() => new(
        PayloadPrivacyValidator.SchemaVersion,
        Today,
        new PayloadCoverage(["checking", "credit card"], 6),
        [
            new PayloadMetric(AiPayloadBuilder.IncomeMonthlyMean, 250000.00m, AiPayloadBuilder.Usd),
            new PayloadMetric(AiPayloadBuilder.SavingsRate, null, AiPayloadBuilder.Ratio),
        ],
        [new PayloadCategory("TRAVEL", 123456.78m)],
        [new PayloadRecurring("ENTERTAINMENT", "monthly", 15.49m, true)]);

    private static decimal? Metric(AiPayload payload, string id) =>
        Assert.Single(payload.Metrics, metric => metric.Id == id).Value;

    private async Task<AiPayloadResult> BuildAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var builder = scope.ServiceProvider.GetRequiredService<AiPayloadBuilder>();
        return await builder.BuildAsync(userId, Today, CancellationToken.None);
    }

    /// <summary>
    /// Three complete months (Aug to Oct) of the same pattern, one row in the current month and one row
    /// outside the six-month window. Descriptions are deliberately full of identifying text.
    /// </summary>
    private async Task<Guid> SeedJaneAsync()
    {
        var userId = await AddUserAsync("Jane Doe");
        var checking = await AddAccountAsync(
            userId, "Jane's Everyday Checking", "First Platypus Bank", "6789", "depository", "checking", 1234.56m);
        var card = await AddAccountAsync(userId, "Platinum Rewards Card", "First Platypus Bank", "4242", "credit", "credit card", null);

        foreach (var month in new[] { 8, 9, 10 })
        {
            var date = new DateOnly(2026, month, 5);
            await AddRowAsync(userId, checking, 3000m, date, "INCOME", "INCOME_WAGES", "PAYROLL ACME CORP jane.doe@gmail.com");
            await AddRowAsync(userId, card, -15.49m, date, "ENTERTAINMENT", null, "NETFLIX.COM 866-579-7172");
            await AddRowAsync(userId, checking, -200m, date, "TRANSFER_OUT", TransactionCategory.OtherTransferOut, "ZELLE TO JOHN SMITH 4111111111111111");
            await AddRowAsync(userId, checking, -500m, date, "TRANSFER_OUT", "TRANSFER_OUT_ACCOUNT_TRANSFER", "TO SAVINGS");
            await AddRowAsync(userId, card, -84.51m, date, "FOOD_AND_DRINK", null, "KROGER #123");
        }

        await AddRowAsync(userId, card, -1000m, new DateOnly(2026, 11, 1), "FOOD_AND_DRINK", null, "WHOLE FOODS MKT 10234");
        await AddRowAsync(userId, checking, -999m, new DateOnly(2026, 4, 20), "OTHER", null, "OLD THING");
        return userId;
    }

    private async Task<Guid> AddUserAsync(string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new User
        {
            Email = $"{name.Replace(' ', '.').ToLowerInvariant()}.{Guid.NewGuid():N}@example.com",
            Name = name,
            EmailVerifiedAt = DateTime.UtcNow,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<Guid> AddAccountAsync(
        Guid userId, string name, string? institution, string? mask, string? type, string? subtype, decimal? balance)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = new Account
        {
            UserId = userId,
            Name = name,
            Institution = institution,
            Mask = mask,
            Type = type,
            Subtype = subtype,
            CurrentBalance = balance,
        };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return account.Id;
    }

    private async Task AddRowAsync(
        Guid userId, Guid accountId, decimal amount, DateOnly date, string? category, string? subcategory, string description)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Transactions.Add(new Transaction
        {
            UserId = userId,
            AccountId = accountId,
            Source = TransactionSource.Seed,
            Date = date,
            Description = description,
            Amount = amount,
            Category = category,
            Subcategory = subcategory,
        });
        await db.SaveChangesAsync();
    }
}
