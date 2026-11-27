using backend.Data;
using backend.DTOs;
using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Services;

/// <summary>
/// Builds the outbound AI payload from an allowlist: each field is a number, a date or one of our own codes,
/// written here on purpose. Nothing is redacted, because identifying text is never copied in to begin with.
/// Rows follow the summary rules (<see cref="TransactionCategory.CountedInSummaries"/> and
/// <see cref="TransactionCategory.IsSpending"/>). Also builds the privacy report the preview screen shows.
/// </summary>
public sealed class AiPayloadBuilder(AppDbContext db)
{
    public const string IncomeMonthlyMean = "income_monthly_mean";
    public const string SpendingMonthlyMean = "spending_monthly_mean";
    public const string NetCashFlowMean = "net_cash_flow_mean";
    public const string SavingsRate = "savings_rate";
    public const string Usd = "USD";
    public const string Ratio = "ratio";

    /// <summary>The account kinds the payload may name. A Plaid subtype outside this set falls back to its type.</summary>
    public static readonly IReadOnlyList<string> AccountTypes =
        ["checking", "savings", "credit card", "depository", "credit", "loan", "investment", "other"];

    /// <summary>Averages use up to this many complete calendar months; the current month is always partial.</summary>
    private const int MetricMonths = 6;

    /// <summary>Same window as the Bills endpoint, so a yearly charge can be seen more than once.</summary>
    private const int RecurringWindowMonths = 13;

    private const int ExampleCount = 3;

    private static readonly Func<Transaction, bool> IsSpending = TransactionCategory.IsSpending.Compile();

    /// <summary>
    /// One read of the user's counted rows for the last <see cref="RecurringWindowMonths"/> months, then
    /// everything in memory. That is a few thousand rows at most for one person.
    /// </summary>
    public async Task<AiPayloadResult> BuildAsync(Guid userId, DateOnly today, CancellationToken cancellationToken)
    {
        var user = await db.Users
            .AsNoTracking()
            .Where(candidate => candidate.Id == userId)
            .Select(candidate => new { candidate.Name, candidate.Email })
            .SingleAsync(cancellationToken);

        // Statement accounts stay out until merged (see Account.IncludeInTotals).
        var accounts = await db.Accounts
            .AsNoTracking()
            .Where(account => account.UserId == userId && account.IncludeInTotals)
            .ToListAsync(cancellationToken);

        var windowStart = today.AddMonths(-RecurringWindowMonths);
        var rows = await db.Transactions
            .AsNoTracking()
            .Where(transaction => transaction.UserId == userId)
            .Where(TransactionCategory.CountedInSummaries)
            .Where(transaction => transaction.Account.IncludeInTotals)
            .Where(transaction => transaction.Date >= windowStart && transaction.Date <= today)
            .ToListAsync(cancellationToken);

        var payload = new AiPayload(
            PayloadPrivacyValidator.SchemaVersion,
            today,
            new PayloadCoverage(
                accounts.Select(account => AccountType(account.Type, account.Subtype)).Distinct().Order().ToList(),
                0),
            [],
            [],
            Recurring(rows, today));
        payload = WithMonthlyFigures(payload, rows, today);

        var identifiers = rows.Select(row => row.Description)
            .Concat(accounts.Select(account => account.Name))
            .Concat(accounts.Select(account => account.Institution).OfType<string>())
            .Concat(accounts.Select(account => account.Mask).OfType<string>())
            .Append(user.Name)
            .Append(user.Email);
        var checks = PayloadPrivacyValidator.Validate(payload, identifiers);

        var report = new PayloadPrivacyReport(
            checks.All(check => check.Passed),
            Examples(rows, accounts),
            [
                new WithheldField("Transaction descriptions", rows.Count),
                new WithheldField("Payees on person-to-person transfers",
                    rows.Count(row => row.Subcategory == TransactionCategory.OtherTransferOut)),
                new WithheldField("Account names", accounts.Count),
                new WithheldField("Bank names", accounts.Select(account => account.Institution).OfType<string>().Distinct().Count()),
                new WithheldField("Account number endings", accounts.Count(account => account.Mask is not null)),
                new WithheldField("Account balances", accounts.Count(account => account.CurrentBalance is not null)),
            ],
            checks);

        return new AiPayloadResult(payload, report);
    }

    /// <summary>
    /// Income, spending, net cash flow and per-category means over the complete months that have data, so a
    /// user who linked a bank two months ago is averaged over two months, not six.
    /// </summary>
    private static AiPayload WithMonthlyFigures(AiPayload payload, List<Transaction> rows, DateOnly today)
    {
        var end = new DateOnly(today.Year, today.Month, 1);
        var start = end.AddMonths(-MetricMonths);
        var counted = rows.Where(row => row.Date >= start && row.Date < end).ToList();
        if (counted.Count == 0)
        {
            return payload with
            {
                Metrics =
                [
                    new PayloadMetric(IncomeMonthlyMean, null, Usd),
                    new PayloadMetric(SpendingMonthlyMean, null, Usd),
                    new PayloadMetric(NetCashFlowMean, null, Usd),
                    new PayloadMetric(SavingsRate, null, Ratio),
                ],
            };
        }

        var first = counted.Min(row => row.Date);
        var months = (end.Year - first.Year) * 12 + end.Month - first.Month;

        var income = counted.Where(row => !IsSpending(row)).Sum(row => row.Amount);
        var net = counted.Sum(row => row.Amount);

        var categories = counted
            .Where(IsSpending)
            .GroupBy(row => row.Category ?? TransactionCategory.Other)
            .Select(group => new PayloadCategory(group.Key, Money(-group.Sum(row => row.Amount) / months)))
            .Where(category => category.MonthlyMean != 0m)
            .OrderByDescending(category => category.MonthlyMean)
            .ToList();

        return payload with
        {
            Coverage = payload.Coverage with { MonthsComplete = months },
            Metrics =
            [
                new PayloadMetric(IncomeMonthlyMean, Money(income / months), Usd),
                new PayloadMetric(SpendingMonthlyMean, Money((income - net) / months), Usd),
                new PayloadMetric(NetCashFlowMean, Money(net / months), Usd),
                new PayloadMetric(SavingsRate,
                    income > 0m ? Math.Round(net / income, 3, MidpointRounding.AwayFromZero) : null, Ratio),
            ],
            Categories = categories,
        };
    }

    /// <summary>The Bills page's streams with the merchant dropped: category, cadence and cost only.</summary>
    private static List<PayloadRecurring> Recurring(List<Transaction> rows, DateOnly today)
    {
        var charges = rows
            .Where(IsSpending)
            .Where(row => row.Amount < 0)
            .Select(row => new RecurringCandidate(row.Date, row.Description, -row.Amount, row.Category));

        return RecurringDetector.Detect(charges, today)
            .Select(stream => new PayloadRecurring(
                stream.Category ?? TransactionCategory.Other, stream.Cadence, stream.MonthlyCost, stream.IsSubscription))
            .OrderByDescending(stream => stream.MonthlyCost)
            .ToList();
    }

    /// <summary>The newest rows, one per bucket, so the preview shows several kinds of row being reduced.</summary>
    private static List<PrivacyExample> Examples(List<Transaction> rows, List<Account> accounts)
    {
        var byId = accounts.ToDictionary(account => account.Id);
        return rows
            .OrderByDescending(row => row.Date)
            .Select(row => (Row: row, SentAs: IsSpending(row) ? row.Category ?? TransactionCategory.Other : TransactionCategory.Income))
            .DistinctBy(pair => pair.SentAs)
            .Take(ExampleCount)
            .Select(pair => new PrivacyExample(
                pair.Row.Date,
                pair.Row.Description,
                byId.TryGetValue(pair.Row.AccountId, out var account) ? account.Name : "",
                account?.Mask,
                pair.Row.Amount,
                pair.SentAs))
            .ToList();
    }

    private static string AccountType(string? type, string? subtype)
    {
        if (subtype is not null && AccountTypes.Contains(subtype))
        {
            return subtype;
        }

        return type is not null && AccountTypes.Contains(type) ? type : "other";
    }

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
