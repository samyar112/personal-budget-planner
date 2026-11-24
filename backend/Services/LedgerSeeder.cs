using backend.Data;
using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Services;

/// <summary>
/// Sample ledger rows for tests. Nothing runs it at boot: the app's ledger holds only real data. Fills
/// a user's ledger only when it is empty, so running it twice, or over real rows, changes nothing.
/// </summary>
public sealed class LedgerSeeder(AppDbContext db)
{
    public async Task SeedAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (await db.Transactions.AnyAsync(transaction => transaction.UserId == userId, cancellationToken))
        {
            return;
        }

        var account = await db.Accounts.FirstOrDefaultAsync(candidate => candidate.UserId == userId, cancellationToken);
        if (account is null)
        {
            account = new Account { UserId = userId, Name = "Sample Checking", Institution = "Sample Bank" };
            db.Accounts.Add(account);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var first = new DateOnly(today.Year, today.Month, 1).AddMonths(-2);
        var rows = new List<Transaction>();

        for (var month = 0; month < 3; month++)
        {
            var start = first.AddMonths(month);
            Add(rows, userId, account, start.AddDays(0), "Employer payroll", 3200m, "INCOME");
            Add(rows, userId, account, start.AddDays(1), "Rent", -1250m, "RENT_AND_UTILITIES");
            Add(rows, userId, account, start.AddDays(4), "Electric utility", -86.40m - month * 3, "RENT_AND_UTILITIES");
            Add(rows, userId, account, start.AddDays(6), "Streaming subscription", -15.99m, "ENTERTAINMENT");
            Add(rows, userId, account, start.AddDays(8), "Grocery store", -112.35m, "FOOD_AND_DRINK");
            Add(rows, userId, account, start.AddDays(11), "Coffee shop", -5.40m, "FOOD_AND_DRINK");
            Add(rows, userId, account, start.AddDays(14), "Grocery store", -94.10m - month * 4, "FOOD_AND_DRINK");
            Add(rows, userId, account, start.AddDays(17), "Gas station", -48.00m, "TRANSPORTATION");
            Add(rows, userId, account, start.AddDays(20), "Restaurant", -42.75m, "FOOD_AND_DRINK");
            Add(rows, userId, account, start.AddDays(24), "Savings transfer", -300m, "TRANSFER_OUT");
        }

        db.Transactions.AddRange(rows.Where(row => row.Date <= today));
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Seeds every verified account whose ledger is empty. Run at boot in Development.</summary>
    public async Task SeedVerifiedUsersAsync(CancellationToken cancellationToken)
    {
        var userIds = await db.Users
            .Where(user => user.EmailVerifiedAt != null)
            .Select(user => user.Id)
            .ToListAsync(cancellationToken);

        foreach (var userId in userIds)
        {
            await SeedAsync(userId, cancellationToken);
        }
    }

    private static void Add(
        List<Transaction> rows, Guid userId, Account account, DateOnly date,
        string description, decimal amount, string category) =>
        rows.Add(new Transaction
        {
            UserId = userId,
            Account = account,
            Source = TransactionSource.Seed,
            Description = description,
            Amount = amount,
            Date = date,
            Category = category,
        });
}
