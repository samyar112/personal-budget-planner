using System.Globalization;
using backend.DTOs;

namespace backend.Services;

/// <summary>
/// What the score is computed from, already reduced to monthly means and balance totals by
/// <see cref="HealthScoreService"/>. A null balance means no account of that kind has one, which is
/// different from a balance of zero.
/// </summary>
public sealed record HealthInputs(
    int MonthsComplete,
    decimal IncomeMonthly,
    decimal SpendingMonthly,
    int PositiveMonths,
    int FeeCount,
    decimal DebtPaymentsMonthly,
    decimal? LiquidBalance,
    decimal? LongTermBalance,
    decimal? CardBalance,
    decimal? CardLimit,
    bool InsuranceSeen);

/// <summary>
/// The scoring rules, and nothing else: no database, no clock, so every threshold can be tested at its edge.
/// Modelled on the FinHealth framework's eight indicators. Its survey questions that the ledger cannot answer
/// (credit score, insurance confidence, planning) are replaced by stand-ins and flagged as estimated.
/// Each indicator scales linearly between the value that scores 0 and the value that scores 100.
/// </summary>
public static class HealthScore
{
    public const string Healthy = "Healthy";
    public const string Coping = "Coping";
    public const string Vulnerable = "Vulnerable";

    public const int HealthyFrom = 80;
    public const int CopingFrom = 40;

    public const decimal SavingsRateFull = 0.20m;
    public const int FeesForZero = 3;
    public const decimal BufferMonthsFull = 6m;
    public const decimal LongTermYearsFull = 1m;
    public const decimal DebtRatioFull = 0.15m;
    public const decimal DebtRatioZero = 0.43m;
    public const decimal UtilisationFull = 0.10m;
    public const decimal UtilisationZero = 0.75m;

    public const int FeeWindowMonths = 6;
    public const int InsuranceWindowMonths = 13;

    public static HealthScoreResponse Compute(HealthInputs inputs)
    {
        var hasMonths = inputs.MonthsComplete > 0;
        var hasIncome = hasMonths && inputs.IncomeMonthly > 0m;

        HealthComponent[] components =
        [
            new("spend", "Spend", null, [SpendLessThanIncome(inputs, hasIncome), BillsOnTime(inputs, hasMonths)]),
            new("save", "Save", null, [LiquidSavings(inputs, hasMonths), LongTermSavings(inputs, hasIncome)]),
            new("borrow", "Borrow", null, [ManageableDebt(inputs, hasIncome), CardUtilisation(inputs)]),
            new("plan", "Plan", null, [Insurance(inputs, hasMonths), PlansAhead(inputs, hasMonths)]),
        ];
        components = components.Select(component => component with { Score = Mean(component.Indicators) }).ToArray();

        var all = components.SelectMany(component => component.Indicators).ToList();
        var score = hasIncome ? Mean(all) : null;
        return new HealthScoreResponse(
            score,
            score is { } value ? Tier(value) : null,
            all.Count(indicator => indicator.Score is not null),
            all.Count,
            inputs.MonthsComplete,
            components);
    }

    public static string Tier(int score) => score >= HealthyFrom ? Healthy : score >= CopingFrom ? Coping : Vulnerable;

    private static HealthIndicator SpendLessThanIncome(HealthInputs inputs, bool hasIncome)
    {
        const string method =
            "Income minus spending, as a share of income, averaged over your complete months. 0% or less scores 0; "
            + "20% or more scores 100.";
        if (!hasIncome)
        {
            return new("spend_less_than_income", "Spend less than you earn", null,
                "No income in a complete month yet.", method, false);
        }

        var rate = (inputs.IncomeMonthly - inputs.SpendingMonthly) / inputs.IncomeMonthly;
        var reason = rate >= 0m
            ? $"You keep {Percent(rate)} of your income."
            : $"You spend {Percent(-rate)} more than you earn.";
        return new("spend_less_than_income", "Spend less than you earn",
            Linear(rate, 0m, SavingsRateFull), reason, method, false);
    }

    private static HealthIndicator BillsOnTime(HealthInputs inputs, bool hasMonths)
    {
        const string method =
            "Overdraft, insufficient-funds and late fees in the last 6 months. None scores 100; each fee takes a "
            + "third off; 3 or more scores 0.";
        if (!hasMonths)
        {
            return new("bills_on_time", "Pay bills on time", null, "Not enough history yet.", method, false);
        }

        var reason = inputs.FeeCount == 0
            ? "No overdraft, insufficient-funds or late fees in the last 6 months."
            : $"{inputs.FeeCount} overdraft, insufficient-funds or late {(inputs.FeeCount == 1 ? "fee" : "fees")} in the last 6 months.";
        return new("bills_on_time", "Pay bills on time",
            Linear(inputs.FeeCount, FeesForZero, 0m), reason, method, false);
    }

    private static HealthIndicator LiquidSavings(HealthInputs inputs, bool hasMonths)
    {
        const string method =
            "Checking and savings balances divided by your average monthly spending. 0 months scores 0; "
            + "6 months or more scores 100.";
        if (inputs.LiquidBalance is not { } balance)
        {
            return new("liquid_savings", "Savings you can reach", null,
                "Link a checking or savings account that reports a balance.", method, false);
        }

        if (!hasMonths || inputs.SpendingMonthly <= 0m)
        {
            return new("liquid_savings", "Savings you can reach", null,
                "No spending in a complete month yet.", method, false);
        }

        var months = Math.Max(balance, 0m) / inputs.SpendingMonthly;
        return new("liquid_savings", "Savings you can reach", Linear(months, 0m, BufferMonthsFull),
            $"Checking and savings would cover {Number(months)} months of spending.", method, false);
    }

    private static HealthIndicator LongTermSavings(HealthInputs inputs, bool hasIncome)
    {
        const string method =
            "Investment and retirement balances divided by a year's income. None scores 0; a year's income or "
            + "more scores 100.";
        if (inputs.LongTermBalance is not { } balance)
        {
            return new("long_term_savings", "Long-term savings", null,
                "No investment or retirement account linked.", method, false);
        }

        if (!hasIncome)
        {
            return new("long_term_savings", "Long-term savings", null,
                "No income in a complete month yet.", method, false);
        }

        var years = Math.Max(balance, 0m) / (inputs.IncomeMonthly * 12m);
        return new("long_term_savings", "Long-term savings", Linear(years, 0m, LongTermYearsFull),
            $"Investments and retirement savings equal {Number(years)} years of income.", method, false);
    }

    private static HealthIndicator ManageableDebt(HealthInputs inputs, bool hasIncome)
    {
        const string method =
            "Loan payments (mortgage, student, car, personal; not card payments) as a share of income. 15% or "
            + "less scores 100; 43% or more scores 0.";
        if (!hasIncome)
        {
            return new("manageable_debt", "Manageable debt", null,
                "No income in a complete month yet.", method, false);
        }

        var ratio = inputs.DebtPaymentsMonthly / inputs.IncomeMonthly;
        var reason = inputs.DebtPaymentsMonthly <= 0m
            ? "No loan payments found."
            : $"Loan payments take {Percent(ratio)} of your income.";
        return new("manageable_debt", "Manageable debt",
            Linear(ratio, DebtRatioZero, DebtRatioFull), reason, method, false);
    }

    private static HealthIndicator CardUtilisation(HealthInputs inputs)
    {
        const string method =
            "Card balances divided by card limits. 10% or less scores 100; 75% or more scores 0. Stands in for "
            + "a credit score, which we cannot see.";
        if (inputs.CardBalance is not { } balance || inputs.CardLimit is not { } limit || limit <= 0m)
        {
            return new("credit_utilisation", "Card utilisation", null,
                "No credit card with a reported limit linked.", method, true);
        }

        var used = Math.Max(balance, 0m) / limit;
        return new("credit_utilisation", "Card utilisation", Linear(used, UtilisationZero, UtilisationFull),
            $"Your cards are {Percent(used)} used.", method, true);
    }

    private static HealthIndicator Insurance(HealthInputs inputs, bool hasMonths)
    {
        const string method =
            "Whether an insurance payment appears in the last 13 months: 100 if one does, 0 if not. Stands in "
            + "for a survey question about cover.";
        if (!hasMonths)
        {
            return new("insurance", "Insurance", null, "Not enough history yet.", method, true);
        }

        return inputs.InsuranceSeen
            ? new("insurance", "Insurance", 100, "Insurance payments found.", method, true)
            : new("insurance", "Insurance", 0,
                "No insurance payments seen. Cover paid through your employer will not show here.", method, true);
    }

    private static HealthIndicator PlansAhead(HealthInputs inputs, bool hasMonths)
    {
        const string method =
            "Share of your complete months (up to 6) in which income was more than spending. Stands in for a "
            + "survey question about planning ahead.";
        if (!hasMonths)
        {
            return new("plans_ahead", "Plan ahead", null, "Not enough history yet.", method, true);
        }

        return new("plans_ahead", "Plan ahead",
            Linear(inputs.PositiveMonths, 0m, inputs.MonthsComplete),
            $"Income beat spending in {inputs.PositiveMonths} of {inputs.MonthsComplete} months.", method, true);
    }

    /// <summary>0 at <paramref name="zeroAt"/>, 100 at <paramref name="fullAt"/>, straight line between. Either may be the larger.</summary>
    private static int Linear(decimal value, decimal zeroAt, decimal fullAt)
    {
        var share = Math.Clamp((value - zeroAt) / (fullAt - zeroAt), 0m, 1m);
        return (int)Math.Round(share * 100m, MidpointRounding.AwayFromZero);
    }

    private static int? Mean(IEnumerable<HealthIndicator> indicators)
    {
        var scores = indicators.Select(indicator => indicator.Score).OfType<int>().ToList();
        return scores.Count == 0
            ? null
            : (int)Math.Round((decimal)scores.Sum() / scores.Count, MidpointRounding.AwayFromZero);
    }

    private static string Percent(decimal ratio) =>
        Math.Round(ratio * 100m, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "%";

    private static string Number(decimal value) =>
        Math.Round(value, 1, MidpointRounding.AwayFromZero).ToString("0.0", CultureInfo.InvariantCulture);
}
