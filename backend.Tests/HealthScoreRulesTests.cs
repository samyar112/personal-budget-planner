using backend.DTOs;
using backend.Services;

namespace backend.Tests;

/*
 * Test cases:
 * 1. With no complete month, there is no score or tier and no ledger-based indicator is scored.
 * 2. With no income, there is no score even when balances are linked.
 * 3. Spend less than income: 0% or below scores 0, 20% or above scores 100, linear between.
 * 4. Bills on time: each fee takes a third off, and 3 or more score 0.
 * 5. Liquid savings: months of spending covered, 6 months scores 100, an overdrawn balance scores 0.
 * 6. Long-term savings: a year's income scores 100.
 * 7. Manageable debt: 15% of income or less scores 100, 43% or more scores 0.
 * 8. Card utilisation: 10% or less scores 100, 75% or more scores 0, and it is flagged as estimated.
 * 9. An indicator without data is left out: not scored, not counted, and the score is the mean of the rest.
 * 10. Insurance scores 100 when seen and 0 when not.
 * 11. Plan ahead is the share of complete months with positive cash flow.
 * 12. Tiers: 80 and above is Healthy, 40 to 79 is Coping, below 40 is Vulnerable.
 * 13. Every indicator carries a non-empty method, so the legend always has an explanation.
 */
public sealed class HealthScoreRulesTests
{
    /// <summary>Six months, a 20% savings rate, no fees, no debt, insurance seen, every month positive; no balances.</summary>
    private static readonly HealthInputs Base = new(
        MonthsComplete: 6,
        IncomeMonthly: 4000m,
        SpendingMonthly: 3200m,
        PositiveMonths: 6,
        FeeCount: 0,
        DebtPaymentsMonthly: 0m,
        LiquidBalance: null,
        LongTermBalance: null,
        CardBalance: null,
        CardLimit: null,
        InsuranceSeen: true);

    [Fact]
    public void NoCompleteMonth_HasNoScore()
    {
        var result = HealthScore.Compute(Base with
        {
            MonthsComplete = 0, IncomeMonthly = 0m, SpendingMonthly = 0m, PositiveMonths = 0, LiquidBalance = 500m,
        });

        Assert.Null(result.Score);
        Assert.Null(result.Tier);
        Assert.Equal(0, result.IndicatorsScored);
        Assert.Equal(8, result.IndicatorsTotal);
    }

    [Fact]
    public void NoIncome_HasNoScore_EvenWithBalances()
    {
        var result = HealthScore.Compute(Base with
        {
            IncomeMonthly = 0m, LiquidBalance = 9000m, CardBalance = 100m, CardLimit = 1000m,
        });

        Assert.Null(result.Score);
        Assert.Null(Indicator(result, "spend_less_than_income").Score);
    }

    [Theory]
    [InlineData(4400, 0)]   // spends more than earned
    [InlineData(4000, 0)]   // 0%
    [InlineData(3600, 50)]  // 10%
    [InlineData(3200, 100)] // 20%
    [InlineData(2000, 100)] // 50%
    public void SpendLessThanIncome_ScalesToTwentyPercent(int spending, int expected)
    {
        var result = HealthScore.Compute(Base with { SpendingMonthly = spending });

        Assert.Equal(expected, Indicator(result, "spend_less_than_income").Score);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(1, 67)]
    [InlineData(2, 33)]
    [InlineData(3, 0)]
    [InlineData(5, 0)]
    public void BillsOnTime_EachFeeTakesAThird(int fees, int expected)
    {
        var result = HealthScore.Compute(Base with { FeeCount = fees });

        Assert.Equal(expected, Indicator(result, "bills_on_time").Score);
    }

    [Theory]
    [InlineData(-250, 0)]
    [InlineData(0, 0)]
    [InlineData(9600, 50)]   // 3 months of 3200
    [InlineData(19200, 100)] // 6 months
    [InlineData(40000, 100)]
    public void LiquidSavings_SixMonthsOfSpendingScoresFull(int balance, int expected)
    {
        var result = HealthScore.Compute(Base with { LiquidBalance = balance });

        Assert.Equal(expected, Indicator(result, "liquid_savings").Score);
    }

    [Theory]
    [InlineData(24000, 50)]  // half of 48,000
    [InlineData(48000, 100)]
    public void LongTermSavings_AYearsIncomeScoresFull(int balance, int expected)
    {
        var result = HealthScore.Compute(Base with { LongTermBalance = balance });

        Assert.Equal(expected, Indicator(result, "long_term_savings").Score);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(600, 100)]  // 15%
    [InlineData(1160, 50)]  // 29%
    [InlineData(1720, 0)]   // 43%
    [InlineData(2500, 0)]
    public void ManageableDebt_FifteenToFortyThreePercent(int payments, int expected)
    {
        var result = HealthScore.Compute(Base with { DebtPaymentsMonthly = payments });

        Assert.Equal(expected, Indicator(result, "manageable_debt").Score);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 100)] // 10%
    [InlineData(425, 50)]  // 42.5%
    [InlineData(750, 0)]   // 75%
    [InlineData(1200, 0)]
    public void CardUtilisation_TenToSeventyFivePercent(int balance, int expected)
    {
        var result = HealthScore.Compute(Base with { CardBalance = balance, CardLimit = 1000m });

        var indicator = Indicator(result, "credit_utilisation");
        Assert.Equal(expected, indicator.Score);
        Assert.True(indicator.Estimated);
    }

    [Fact]
    public void MissingIndicators_AreLeftOutOfTheMean()
    {
        // Scored: spend 100, bills 100, debt 100, insurance 0, plans 100. No balances, so three are left out.
        var result = HealthScore.Compute(Base with { InsuranceSeen = false });

        Assert.Equal(5, result.IndicatorsScored);
        Assert.Equal(80, result.Score);
        Assert.Null(Indicator(result, "liquid_savings").Score);
        Assert.Null(result.Components.Single(component => component.Id == "save").Score);
        Assert.Equal(50, result.Components.Single(component => component.Id == "plan").Score);
    }

    [Fact]
    public void Insurance_ScoresWhenSeenOnly()
    {
        Assert.Equal(100, Indicator(HealthScore.Compute(Base), "insurance").Score);
        Assert.Equal(0, Indicator(HealthScore.Compute(Base with { InsuranceSeen = false }), "insurance").Score);
    }

    [Theory]
    [InlineData(6, 6, 100)]
    [InlineData(4, 6, 67)]
    [InlineData(0, 3, 0)]
    public void PlansAhead_IsShareOfPositiveMonths(int positive, int months, int expected)
    {
        var result = HealthScore.Compute(Base with { PositiveMonths = positive, MonthsComplete = months });

        Assert.Equal(expected, Indicator(result, "plans_ahead").Score);
    }

    [Theory]
    [InlineData(100, "Healthy")]
    [InlineData(80, "Healthy")]
    [InlineData(79, "Coping")]
    [InlineData(40, "Coping")]
    [InlineData(39, "Vulnerable")]
    [InlineData(0, "Vulnerable")]
    public void Tier_UsesFinHealthCutOffs(int score, string expected)
    {
        Assert.Equal(expected, HealthScore.Tier(score));
    }

    [Fact]
    public void EveryIndicator_HasAMethod()
    {
        var result = HealthScore.Compute(Base);

        var indicators = result.Components.SelectMany(component => component.Indicators).ToList();
        Assert.Equal(8, indicators.Count);
        Assert.All(indicators, indicator => Assert.False(string.IsNullOrWhiteSpace(indicator.Method)));
    }

    private static HealthIndicator Indicator(HealthScoreResponse result, string id) =>
        result.Components.SelectMany(component => component.Indicators).Single(indicator => indicator.Id == id);
}
