using System.Text.RegularExpressions;
using backend.Models;

namespace backend.Services;

/// <summary>One spending row as the detector sees it. <c>Amount</c> is money out as a positive number.</summary>
public sealed record RecurringCandidate(DateOnly Date, string Description, decimal Amount, string? Category);

/// <summary>A detected repeating charge. <c>MonthlyCost</c> is the typical amount scaled to one month.</summary>
public sealed record RecurringStream(
    string Merchant,
    string? Category,
    string Cadence,
    decimal TypicalAmount,
    decimal LastAmount,
    DateOnly LastDate,
    DateOnly NextDate,
    int Occurrences,
    decimal MonthlyCost,
    bool IsSubscription);

/// <summary>
/// Finds repeating charges in spending rows. Pure: no database, and "today" is passed in.
/// A stream is one normalised merchant with at least <see cref="MinOccurrences"/> charges, a steady
/// interval matching a known cadence, and a steady amount. The thresholds are design choices, not
/// research results; they are named here so they can be argued with.
/// </summary>
public static partial class RecurringDetector
{
    private const int MinOccurrences = 3;

    /// <summary>Share of intervals that must sit inside the cadence's tolerance. One late charge spoils two intervals.</summary>
    private const double MinIntervalShare = 0.6;

    /// <summary>Share of charges whose amount must sit within the tolerance of the median.</summary>
    private const double MinAmountShare = 0.75;

    private const decimal AmountTolerancePercent = 0.10m;
    private const decimal AmountToleranceDollars = 2m;
    private const decimal SubscriptionCeiling = 100m;

    private sealed record Cadence(string Name, int Days, int Tolerance, int PerYear, Func<DateOnly, DateOnly> Next);

    // Calendar steps for monthly and longer, so a bill on the 31st does not drift.
    private static readonly Cadence[] Cadences =
    [
        new("weekly", 7, 1, 52, date => date.AddDays(7)),
        new("biweekly", 14, 2, 26, date => date.AddDays(14)),
        new("monthly", 30, 4, 12, date => date.AddMonths(1)),
        new("quarterly", 91, 10, 4, date => date.AddMonths(3)),
        new("yearly", 365, 15, 1, date => date.AddYears(1)),
    ];

    private static readonly HashSet<string> SubscriptionCategories =
        [TransactionCategory.Entertainment, TransactionCategory.GeneralServices];

    [GeneratedRegex("[^A-Z&]+")]
    private static partial Regex NonLetters();

    /// <summary>Uppercase, digits and punctuation dropped, first three words: "NETFLIX.COM 8821" is "NETFLIX COM".</summary>
    public static string NormalizeMerchant(string description)
    {
        var words = NonLetters().Replace(description.ToUpperInvariant(), " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(3);
        return string.Join(' ', words);
    }

    /// <summary>Active streams, soonest next charge first. Overdue streams are treated as cancelled.</summary>
    public static IReadOnlyList<RecurringStream> Detect(IEnumerable<RecurringCandidate> rows, DateOnly today)
    {
        var streams = new List<RecurringStream>();
        var groups = rows
            .GroupBy(row => NormalizeMerchant(row.Description))
            .Where(group => group.Key.Length > 0);

        foreach (var group in groups)
        {
            var charges = group.OrderBy(row => row.Date).ToList();
            if (charges.Count < MinOccurrences)
            {
                continue;
            }

            var intervals = charges.Zip(charges.Skip(1), (a, b) => b.Date.DayNumber - a.Date.DayNumber).ToList();
            var medianInterval = Median(intervals.Select(days => (decimal)days).ToList());
            var cadence = Cadences.FirstOrDefault(candidate => Math.Abs(medianInterval - candidate.Days) <= candidate.Tolerance);
            if (cadence is null
                || Share(intervals, days => Math.Abs(days - cadence.Days) <= cadence.Tolerance) < MinIntervalShare)
            {
                continue;
            }

            var typical = Median(charges.Select(row => row.Amount).ToList());
            var allowed = Math.Max(typical * AmountTolerancePercent, AmountToleranceDollars);
            if (Share(charges, row => Math.Abs(row.Amount - typical) <= allowed) < MinAmountShare)
            {
                continue;
            }

            var last = charges[^1];
            var next = cadence.Next(last.Date);
            if (today > next.AddDays(cadence.Tolerance))
            {
                continue;
            }

            streams.Add(new RecurringStream(
                last.Description,
                last.Category,
                cadence.Name,
                typical,
                last.Amount,
                last.Date,
                next,
                charges.Count,
                Math.Round(typical * cadence.PerYear / 12m, 2),
                cadence.Name is "monthly" or "yearly"
                    && typical < SubscriptionCeiling
                    && last.Category is not null
                    && SubscriptionCategories.Contains(last.Category)));
        }

        return streams.OrderBy(stream => stream.NextDate).ThenBy(stream => stream.Merchant).ToList();
    }

    private static decimal Median(List<decimal> values)
    {
        values.Sort();
        var middle = values.Count / 2;
        return values.Count % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2;
    }

    private static double Share<T>(IReadOnlyCollection<T> items, Func<T, bool> predicate) =>
        (double)items.Count(predicate) / items.Count;
}
