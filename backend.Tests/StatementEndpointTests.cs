using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using backend.Data;
using backend.DTOs;
using backend.Models;
using backend.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace backend.Tests;

/*
 * Test cases:
 * 1. Upload without a session returns 401.
 * 2. Upload by an unverified user returns 403.
 * 3. A statement in the supported layout becomes PdfUpload rows, categorised, on a statement account that
 *    is excluded from totals and carries only the last four digits.
 * 4. Uploading the same statement again adds nothing; two identical lines in one statement stay two rows.
 * 5. A later statement for the same last four digits lands on the same account.
 * 6. A file that is not a PDF returns 400 and writes nothing.
 * 7. A PDF without the supported layout returns 400 and writes nothing.
 * 8. A dated line that does not parse rejects the whole file; nothing is written.
 * 9. A file over the size cap returns 400.
 * 10. Statement rows change none of: summary, transaction list, spending categories, monthly, merchants,
 *     recurring, health score, AI payload.
 * 11. The statement list shows statement accounts only; the detail returns figures and rows.
 * 12. Another user's statement account, and a non-statement account, are 404 on the detail.
 * 13. Parser: the header repeats per page; footers and totals between rows are skipped.
 */
public sealed class StatementEndpointTests : IClassFixture<AuthWebApplicationFactory>
{
    private const string Password = "Password1!";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly AuthWebApplicationFactory _factory;

    public StatementEndpointTests(AuthWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Upload_WithoutSession_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await UploadAsync(client, Statement("1234", ("01/05/2026", "COFFEE SHOP", "-4.50")));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Upload_ForUnverifiedUser_ReturnsForbidden()
    {
        var (_, email) = await SeedUserAsync(verified: false);
        var client = await SignedInAsync(email);

        var response = await UploadAsync(client, Statement("1234", ("01/05/2026", "COFFEE SHOP", "-4.50")));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Upload_SupportedLayout_WritesPdfRowsOnExcludedStatementAccount()
    {
        var (userId, email) = await SeedUserAsync();
        var client = await SignedInAsync(email);

        var response = await UploadAsync(client, Statement(
            "4821",
            ("10/01/2026", "PAYROLL ACME CORP", "2,500.00"),
            ("10/03/2026", "STARBUCKS 1123", "-6.25"),
            ("10/09/2026", "SHELL OIL 5521", "-41.10")));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<StatementUploadResponse>();
        Assert.Equal(3, result!.Imported);
        Assert.Equal(0, result.Skipped);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = await db.Accounts.SingleAsync(candidate => candidate.UserId == userId);
        Assert.Equal(result.AccountId, account.Id);
        Assert.False(account.IncludeInTotals);
        Assert.Null(account.PlaidItemId);
        Assert.Equal("4821", account.Mask);

        var rows = await db.Transactions.Where(row => row.UserId == userId).OrderBy(row => row.Date).ToListAsync();
        Assert.All(rows, row => Assert.Equal(TransactionSource.PdfUpload, row.Source));
        Assert.All(rows, row => Assert.NotNull(row.ExternalId));
        Assert.Equal([2500m, -6.25m, -41.10m], rows.Select(row => row.Amount));
        Assert.Equal(new DateOnly(2026, 10, 3), rows[1].Date);
        Assert.Equal("STARBUCKS 1123", rows[1].Description);
        Assert.Equal(TransactionCategory.FoodAndDrink, rows[1].Category);
        Assert.Equal(TransactionCategory.Transportation, rows[2].Category);
    }

    [Fact]
    public async Task Upload_SameStatementTwice_AddsNothingAndKeepsIdenticalLines()
    {
        var (userId, email) = await SeedUserAsync();
        var client = await SignedInAsync(email);
        var pdf = Statement(
            "7001",
            ("10/03/2026", "CORNER DELI", "-8.00"),
            ("10/03/2026", "CORNER DELI", "-8.00"),
            ("10/04/2026", "BOOK STORE", "-20.00"));

        var first = await (await UploadAsync(client, pdf)).Content.ReadFromJsonAsync<StatementUploadResponse>();
        var second = await UploadAsync(client, pdf);

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var again = await second.Content.ReadFromJsonAsync<StatementUploadResponse>();
        Assert.Equal(3, first!.Imported);
        Assert.Equal(0, again!.Imported);
        Assert.Equal(3, again.Skipped);
        Assert.Equal(first.AccountId, again.AccountId);
        Assert.Equal(3, await CountRowsAsync(userId));
    }

    [Fact]
    public async Task Upload_LaterStatementSameMask_LandsOnSameAccount()
    {
        var (userId, email) = await SeedUserAsync();
        var client = await SignedInAsync(email);

        var october = await (await UploadAsync(client, Statement("3300", ("10/03/2026", "CORNER DELI", "-8.00"))))
            .Content.ReadFromJsonAsync<StatementUploadResponse>();
        var november = await (await UploadAsync(client, Statement("3300", ("11/03/2026", "CORNER DELI", "-8.00"))))
            .Content.ReadFromJsonAsync<StatementUploadResponse>();
        var other = await (await UploadAsync(client, Statement("9900", ("11/03/2026", "CORNER DELI", "-8.00"))))
            .Content.ReadFromJsonAsync<StatementUploadResponse>();

        Assert.Equal(october!.AccountId, november!.AccountId);
        Assert.NotEqual(october.AccountId, other!.AccountId);
        Assert.Equal(3, await CountRowsAsync(userId));
    }

    [Fact]
    public async Task Upload_NotAPdf_ReturnsBadRequestAndWritesNothing()
    {
        var (userId, email) = await SeedUserAsync();
        var client = await SignedInAsync(email);

        var response = await UploadAsync(client, "Date,Description,Amount\n10/01/2026,X,-1.00"u8.ToArray());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountRowsAsync(userId));
        Assert.Equal(0, await CountAccountsAsync(userId));
    }

    [Fact]
    public async Task Upload_UnsupportedLayout_ReturnsBadRequestAndWritesNothing()
    {
        var (userId, email) = await SeedUserAsync();
        var client = await SignedInAsync(email);

        var response = await UploadAsync(client, Pdf([["Quarterly newsletter", "Nothing tabular here"]]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadAsStringAsync();
        Assert.Contains("not supported", problem);
        Assert.Equal(0, await CountRowsAsync(userId));
        Assert.Equal(0, await CountAccountsAsync(userId));
    }

    [Fact]
    public async Task Upload_DatedLineThatDoesNotParse_RejectsWholeFile()
    {
        var (userId, email) = await SeedUserAsync();
        var client = await SignedInAsync(email);

        var response = await UploadAsync(client, Statement(
            "1234",
            ("10/01/2026", "GOOD LINE", "-1.00"),
            ("10/02/2026", "NO AMOUNT ON THIS LINE", "")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountRowsAsync(userId));
        Assert.Equal(0, await CountAccountsAsync(userId));
    }

    [Fact]
    public async Task Upload_OverSizeCap_ReturnsBadRequest()
    {
        var (userId, email) = await SeedUserAsync();
        var client = await SignedInAsync(email);
        var bytes = new byte[StatementParser.MaxBytes + 1];
        "%PDF-1.7"u8.CopyTo(bytes);

        var response = await UploadAsync(client, bytes);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountAccountsAsync(userId));
    }

    [Fact]
    public async Task StatementRows_LeaveEveryTotalAndTheAiPayloadUnchanged()
    {
        var (userId, email) = await SeedUserAsync();
        var checking = await AddAccountAsync(userId, "Checking");
        var month = new DateOnly(Today.Year, Today.Month, 1);
        for (var back = 1; back <= 4; back++)
        {
            var date = month.AddMonths(-back);
            await AddRowAsync(userId, checking, 3000m, date.AddDays(1), TransactionCategory.Income, "PAYROLL");
            await AddRowAsync(userId, checking, -1200m, date.AddDays(2), TransactionCategory.RentAndUtilities, "RENT");
            await AddRowAsync(userId, checking, -15.49m, date.AddDays(5), TransactionCategory.Entertainment, "NETFLIX");
        }

        var client = await SignedInAsync(email);
        var from = month.AddMonths(-6).ToString("yyyy-MM-dd");
        var to = Today.ToString("yyyy-MM-dd");
        string[] endpoints =
        [
            $"/api/transactions/summary?from={from}&to={to}",
            $"/api/transactions?from={from}&to={to}",
            $"/api/transactions/spending/categories?from={from}&to={to}",
            $"/api/transactions/spending/monthly?from={from}&to={to}",
            $"/api/transactions/spending/merchants?from={from}&to={to}",
            "/api/transactions/recurring",
            "/api/transactions/health-score",
        ];
        var before = await SnapshotAsync(client, userId, endpoints);

        // Income, spending and a monthly subscription: each would move at least one figure if it leaked.
        var lines = new List<(string, string, string)>();
        for (var back = 1; back <= 4; back++)
        {
            var date = month.AddMonths(-back);
            lines.Add((date.AddDays(1).ToString("MM/dd/yyyy"), "SIDE GIG PAYROLL", "900.00"));
            lines.Add((date.AddDays(6).ToString("MM/dd/yyyy"), "WHOLE FOODS", "-250.00"));
        }

        // Monthly up to today, so recurring detection would report it as a live subscription.
        for (var back = 0; back <= 4; back++)
        {
            lines.Add((Today.AddMonths(-back).ToString("MM/dd/yyyy"), "SPOTIFY", "-11.99"));
        }

        var upload = await UploadAsync(client, Statement("5555", [.. lines]));
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        Assert.Equal(25, await CountRowsAsync(userId));

        var after = await SnapshotAsync(client, userId, endpoints);
        var changed = before.Keys.Where(key => before[key] != after[key]).ToList();
        Assert.True(changed.Count == 0, $"Changed after a statement upload: {string.Join(", ", changed)}");
    }

    [Fact]
    public async Task List_ShowsStatementAccountsOnly_AndDetailReturnsFiguresAndRows()
    {
        var (userId, email) = await SeedUserAsync();
        await AddAccountAsync(userId, "Checking");
        var client = await SignedInAsync(email);
        await UploadAsync(client, Statement(
            "2468",
            ("10/01/2026", "PAYROLL ACME CORP", "1,000.00"),
            ("10/02/2026", "STARBUCKS", "-5.00"),
            ("10/05/2026", "TRANSFER TO SAVINGS", "-300.00")));

        var list = await client.GetFromJsonAsync<List<StatementAccountResponse>>("/api/statements");
        var account = Assert.Single(list!);
        Assert.Equal("2468", account.Mask);
        Assert.Equal(3, account.TransactionCount);
        Assert.Equal(new DateOnly(2026, 10, 1), account.FirstDate);
        Assert.Equal(new DateOnly(2026, 10, 5), account.LastDate);

        var detail = await client.GetFromJsonAsync<StatementDetailResponse>($"/api/statements/{account.Id}");
        Assert.Equal(1000m, detail!.Income);
        Assert.Equal(5m, detail.Expenses);
        Assert.Equal(995m, detail.Net);
        Assert.Equal(3, detail.Transactions.Count);
        Assert.Equal(new DateOnly(2026, 10, 5), detail.Transactions[0].Date);
        Assert.All(detail.Transactions, row => Assert.Equal("PdfUpload", row.Source));
    }

    [Fact]
    public async Task Detail_OtherUsersOrNonStatementAccount_ReturnsNotFound()
    {
        var (_, ownerEmail) = await SeedUserAsync();
        var owner = await SignedInAsync(ownerEmail);
        var uploaded = await (await UploadAsync(owner, Statement("1357", ("10/01/2026", "STARBUCKS", "-5.00"))))
            .Content.ReadFromJsonAsync<StatementUploadResponse>();

        var (intruderId, intruderEmail) = await SeedUserAsync();
        var checking = await AddAccountAsync(intruderId, "Checking");
        var intruder = await SignedInAsync(intruderEmail);

        Assert.Equal(HttpStatusCode.NotFound, (await intruder.GetAsync($"/api/statements/{uploaded!.AccountId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.GetAsync($"/api/statements/{checking}")).StatusCode);
        Assert.Empty((await intruder.GetFromJsonAsync<List<StatementAccountResponse>>("/api/statements"))!);
    }

    [Fact]
    public void Parser_RepeatedHeaderAndFootersBetweenRows_AreHandled()
    {
        var parsed = StatementParser.ParseText(
        [
            "Sample Bank",
            "Account ending in 0042",
            "Date Description Amount",
            "10/01/2026 COFFEE -3.00",
            "Page 1 of 2",
            "Date   Description   Amount",
            "10/02/2026 RENT PAYMENT -$1,200.00",
            "Total -1,203.00",
        ]);

        Assert.Equal("0042", parsed.Mask);
        Assert.Equal(
            [new StatementLine(new DateOnly(2026, 10, 1), "COFFEE", -3m), new StatementLine(new DateOnly(2026, 10, 2), "RENT PAYMENT", -1200m)],
            parsed.Lines);
    }

    private async Task<Dictionary<string, string>> SnapshotAsync(HttpClient client, Guid userId, string[] endpoints)
    {
        var snapshot = new Dictionary<string, string>();
        foreach (var endpoint in endpoints)
        {
            var response = await client.GetAsync(endpoint);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            snapshot[endpoint] = await response.Content.ReadAsStringAsync();
        }

        using var scope = _factory.Services.CreateScope();
        var builder = scope.ServiceProvider.GetRequiredService<AiPayloadBuilder>();
        var payload = await builder.BuildAsync(userId, Today, CancellationToken.None);
        snapshot["ai-payload"] = JsonSerializer.Serialize(payload.Payload);
        return snapshot;
    }

    /// <summary>A statement in the supported layout, columns at fixed x positions like a real table.</summary>
    private static byte[] Statement(string mask, params (string Date, string Description, string Amount)[] lines)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.Letter);
        var y = 740d;
        void Write(double x, string text)
        {
            if (text.Length > 0)
            {
                page.AddText(text, 10, new PdfPoint(x, y), font);
            }
        }

        Write(50, "Sample Bank Statement");
        y -= 16;
        Write(50, $"Account ending in {mask}");
        y -= 24;
        Write(50, "Date");
        Write(130, "Description");
        Write(450, "Amount");
        foreach (var (date, description, amount) in lines)
        {
            y -= 14;
            Write(50, date);
            Write(130, description);
            Write(450, amount);
        }

        return builder.Build();
    }

    /// <summary>A PDF with the given lines of text on each page.</summary>
    private static byte[] Pdf(string[][] pages)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var lines in pages)
        {
            var page = builder.AddPage(PageSize.Letter);
            var y = 740d;
            foreach (var line in lines)
            {
                page.AddText(line, 10, new PdfPoint(50, y), font);
                y -= 14;
            }
        }

        return builder.Build();
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, byte[] bytes)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(file, "file", "statement.pdf");
        return client.PostAsync("/api/statements", content);
    }

    private async Task<int> CountRowsAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Transactions.CountAsync(row => row.UserId == userId);
    }

    private async Task<int> CountAccountsAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Accounts.CountAsync(account => account.UserId == userId);
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
        var account = new Account { UserId = userId, Name = name, Type = "depository", Subtype = "checking", CurrentBalance = 5000m };
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
