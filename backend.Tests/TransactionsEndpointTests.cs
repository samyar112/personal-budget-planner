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
 * 1. Listing without a session returns 401.
 * 2. An unverified user is refused with 403.
 * 3. A verified user gets their own rows, newest first, with the signed amount and source.
 * 4. Another user's rows are never returned, and are not counted in the total.
 * 5. from and to bound the dates inclusively.
 * 6. category filters case-insensitively.
 * 7. Paging returns the requested slice and the full total; pageSize is capped at 100.
 * 8. The body carries no user id.
 * 9. The seeder fills an empty ledger once, as Seed rows, and never touches another user's.
 */
public sealed class TransactionsEndpointTests : IClassFixture<AuthWebApplicationFactory>
{
    private const string Password = "Password1!";

    private readonly AuthWebApplicationFactory _factory;

    public TransactionsEndpointTests(AuthWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task List_WithoutSession_ReturnsUnauthorized()
    {
        var response = await _factory.CreateClient().GetAsync("/api/transactions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_ForUnverifiedUser_ReturnsForbidden()
    {
        var (userId, email) = await SeedUserAsync(verified: false);
        await AddRowsAsync(userId, ("Coffee", -5.40m, "2026-10-01", "Coffee"));
        var client = await SignedInAsync(email);

        var response = await client.GetAsync("/api/transactions");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task List_ReturnsCallersRowsNewestFirst()
    {
        var (userId, email) = await SeedUserAsync();
        await AddRowsAsync(
            userId,
            ("Coffee", -5.40m, "2026-10-01", "Coffee"),
            ("Payroll", 2000m, "2026-10-15", "Income"));
        var client = await SignedInAsync(email);

        var body = await GetAsync(client, "/api/transactions");

        Assert.Equal(2, body.Total);
        Assert.Equal(["Payroll", "Coffee"], body.Items.Select(item => item.Description));
        Assert.Equal(-5.40m, body.Items[1].Amount);
        Assert.Equal("Seed", body.Items[0].Source);
    }

    [Fact]
    public async Task List_NeverReturnsAnotherUsersRows()
    {
        var (aliceId, aliceEmail) = await SeedUserAsync();
        var (bobId, _) = await SeedUserAsync();
        await AddRowsAsync(aliceId, ("Alice rent", -900m, "2026-10-01", "Housing"));
        await AddRowsAsync(bobId, ("Bob secret", -1m, "2026-10-02", "Other"), ("Bob two", -2m, "2026-10-03", "Other"));
        var client = await SignedInAsync(aliceEmail);

        var body = await GetAsync(client, "/api/transactions?pageSize=100");

        Assert.Equal(1, body.Total);
        Assert.Equal(["Alice rent"], body.Items.Select(item => item.Description));
    }

    [Fact]
    public async Task List_DateRange_IsInclusive()
    {
        var (userId, email) = await SeedUserAsync();
        await AddRowsAsync(
            userId,
            ("Before", -1m, "2026-09-30", null),
            ("Start", -1m, "2026-10-01", null),
            ("End", -1m, "2026-10-31", null),
            ("After", -1m, "2026-11-01", null));
        var client = await SignedInAsync(email);

        var body = await GetAsync(client, "/api/transactions?from=2026-10-01&to=2026-10-31");

        Assert.Equal(["End", "Start"], body.Items.Select(item => item.Description));
    }

    [Fact]
    public async Task List_Category_MatchesIgnoringCase()
    {
        var (userId, email) = await SeedUserAsync();
        await AddRowsAsync(
            userId,
            ("Latte", -4m, "2026-10-01", "Coffee"),
            ("Rent", -900m, "2026-10-02", "Housing"),
            ("Unsorted", -3m, "2026-10-03", null));
        var client = await SignedInAsync(email);

        var body = await GetAsync(client, "/api/transactions?category=coffee");

        Assert.Equal(["Latte"], body.Items.Select(item => item.Description));
    }

    [Fact]
    public async Task List_Paging_ReturnsSliceAndTotal_AndCapsPageSize()
    {
        var (userId, email) = await SeedUserAsync();
        await AddRowsAsync(
            userId,
            Enumerable.Range(1, 5)
                .Select(day => ($"Row {day}", -1m, $"2026-10-{day:00}", (string?)null))
                .ToArray());
        var client = await SignedInAsync(email);

        var second = await GetAsync(client, "/api/transactions?page=2&pageSize=2");
        var huge = await GetAsync(client, "/api/transactions?pageSize=100000");

        Assert.Equal(5, second.Total);
        Assert.Equal(["Row 3", "Row 2"], second.Items.Select(item => item.Description));
        Assert.Equal(100, huge.PageSize);
    }

    [Fact]
    public async Task List_Body_HasNoUserId()
    {
        var (userId, email) = await SeedUserAsync();
        await AddRowsAsync(userId, ("Coffee", -5m, "2026-10-01", null));
        var client = await SignedInAsync(email);

        var raw = await (await client.GetAsync("/api/transactions")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("userId", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(userId.ToString(), raw);
    }

    [Fact]
    public async Task Seeder_FillsEmptyLedgerOnce_AndLeavesOthersAlone()
    {
        var (userId, _) = await SeedUserAsync();
        var (otherId, _) = await SeedUserAsync();
        await AddRowsAsync(otherId, ("Mine", -1m, "2026-10-01", null));

        using (var scope = _factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<backend.Services.LedgerSeeder>();
            await seeder.SeedAsync(userId, CancellationToken.None);
            await seeder.SeedAsync(userId, CancellationToken.None);
            await seeder.SeedAsync(otherId, CancellationToken.None);
        }

        using var check = _factory.Services.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var seeded = await db.Transactions.Where(row => row.UserId == userId).ToListAsync();
        Assert.NotEmpty(seeded);
        Assert.All(seeded, row => Assert.Equal(TransactionSource.Seed, row.Source));
        Assert.Equal(seeded.Count, seeded.Select(row => row.Id).Distinct().Count());
        Assert.Single(await db.Transactions.Where(row => row.UserId == otherId).ToListAsync());
        Assert.Equal(1, await db.Accounts.CountAsync(account => account.UserId == userId));
    }

    private async Task<(Guid Id, string Email)> SeedUserAsync(bool verified = true)
    {
        var email = $"user.{Guid.NewGuid():N}@example.com";
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var user = new User
        {
            Email = email,
            Name = "Test User",
            EmailVerifiedAt = verified ? DateTime.UtcNow : null,
        };
        user.PasswordHash = hasher.HashPassword(user, Password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (user.Id, email);
    }

    private async Task AddRowsAsync(Guid userId, params (string Description, decimal Amount, string Date, string? Category)[] rows)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = new Account { UserId = userId, Name = "Checking" };
        db.Accounts.Add(account);
        db.Transactions.AddRange(rows.Select(row => new Transaction
        {
            UserId = userId,
            AccountId = account.Id,
            Source = TransactionSource.Seed,
            Description = row.Description,
            Amount = row.Amount,
            Date = DateOnly.Parse(row.Date),
            Category = row.Category,
        }));
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

    private static async Task<TransactionListResponse> GetAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TransactionListResponse>())!;
    }
}
