using System.Text.RegularExpressions;
using backend.Data;
using backend.DTOs;
using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Services;

/// <summary>
/// Reads one user's accounts and counted rows and reduces them to <see cref="HealthInputs"/>. Monthly means
/// use the same window and rules as the AI payload (<see cref="AiPayloadBuilder"/>): up to 6 complete months,
/// starting from the first month with data, so the score and the tips agree on income and spending.
/// The score is computed on every read and never stored.
/// </summary>
public sealed partial class HealthScoreService(AppDbContext db)
{
    private const int MetricMonths = 6;

    /// <summary>Plaid's detailed codes for a missed or bounced payment.</summary>
    private static readonly HashSet<string> MissedPaymentFees =
        ["BANK_FEES_OVERDRAFT_FEES", "BANK_FEES_INSUFFICIENT_FUNDS"];

    private const string InsuranceSubcategory = "GENERAL_SERVICES_INSURANCE";

    private static readonly Func<Transaction, bool> IsSpending = TransactionCategory.IsSpending.Compile();

    public async Task<HealthScoreResponse> ComputeAsync(Guid userId, DateOnly today, CancellationToken cancellationToken)
    {
        // Statement accounts stay out until merged (see Account.IncludeInTotals).
        var accounts = await db.Accounts
            .AsNoTracking()
            .Where(account => account.UserId == userId && account.IncludeInTotals)
            .Select(account => new { account.Type, account.CurrentBalance, account.CreditLimit })
            .ToListAsync(cancellationToken);

        var windowStart = today.AddMonths(-HealthScore.InsuranceWindowMonths);
        var rows = await db.Transactions
            .AsNoTracking()
            .Where(transaction => transaction.UserId == userId)
            .Where(TransactionCategory.CountedInSummaries)
            .Where(transaction => transaction.Account.IncludeInTotals)
            .Where(transaction => transaction.Date >= windowStart && transaction.Date <= today)
            .ToListAsync(cancellationToken);

        var end = new DateOnly(today.Year, today.Month, 1);
        var counted = rows.Where(row => row.Date >= end.AddMonths(-MetricMonths) && row.Date < end).ToList();
        var months = 0;
        decimal income = 0m, spending = 0m, debt = 0m;
        var positiveMonths = 0;
        if (counted.Count > 0)
        {
            var first = counted.Min(row => row.Date);
            months = (end.Year - first.Year) * 12 + end.Month - first.Month;
            var totalIncome = counted.Where(row => !IsSpending(row)).Sum(row => row.Amount);
            var net = counted.Sum(row => row.Amount);
            income = totalIncome / months;
            spending = (totalIncome - net) / months;
            debt = -counted.Where(row => row.Category == TransactionCategory.LoanPayments).Sum(row => row.Amount) / months;
            var netByMonth = counted
                .GroupBy(row => (row.Date.Year, row.Date.Month))
                .ToDictionary(group => group.Key, group => group.Sum(row => row.Amount));
            positiveMonths = netByMonth.Values.Count(value => value > 0m);
        }

        var feeStart = today.AddMonths(-HealthScore.FeeWindowMonths);
        var fees = rows.Count(row => row.Date >= feeStart && row.Amount < 0m && IsMissedPaymentFee(row));
        var insurance = rows.Any(row => row.Amount < 0m
            && (row.Subcategory == InsuranceSubcategory || InsuranceText().IsMatch(row.Description)));

        var liquid = accounts.Where(account => account.Type == "depository" && account.CurrentBalance is not null).ToList();
        var investments = accounts.Where(account => account.Type == "investment" && account.CurrentBalance is not null).ToList();
        var cards = accounts
            .Where(account => account.Type == "credit" && account.CurrentBalance is not null && account.CreditLimit is not null)
            .ToList();

        return HealthScore.Compute(new HealthInputs(
            months,
            income,
            spending,
            positiveMonths,
            fees,
            debt,
            liquid.Count == 0 ? null : liquid.Sum(account => account.CurrentBalance!.Value),
            investments.Count == 0 ? null : investments.Sum(account => account.CurrentBalance!.Value),
            cards.Count == 0 ? null : cards.Sum(account => account.CurrentBalance!.Value),
            cards.Count == 0 ? null : cards.Sum(account => account.CreditLimit!.Value),
            insurance));
    }

    /// <summary>
    /// Plaid's overdraft and insufficient-funds codes, or fee wording in the description for rows Plaid did not
    /// code that way (keyword-categorised rows, and late fees, which Plaid has no code for).
    /// </summary>
    private static bool IsMissedPaymentFee(Transaction row) =>
        (row.Subcategory is { } subcategory && MissedPaymentFees.Contains(subcategory))
        || MissedPaymentText().IsMatch(row.Description);

    [GeneratedRegex(@"\b(OVERDRAFT|NSF|INSUFFICIENT FUNDS|RETURNED ITEM|LATE (FEE|CHARGE|PAYMENT FEE))\b", RegexOptions.IgnoreCase)]
    private static partial Regex MissedPaymentText();

    [GeneratedRegex(@"\bINSURANCE\b", RegexOptions.IgnoreCase)]
    private static partial Regex InsuranceText();
}
